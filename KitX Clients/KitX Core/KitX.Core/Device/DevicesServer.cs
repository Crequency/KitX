using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Event;
using KitX.Core.Security;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Security;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using KitX.Core.DI;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Device server for HTTP API
/// Phase 5: Simplified implementation using direct WebHostBuilder
/// </summary>
public class DevicesServer : ServerBase, IDeviceServer
{
    /// <summary>
    /// Gets the singleton instance (resolves from ServiceHost when available).
    /// Internal code should use constructor injection instead.
    /// </summary>
    public static DevicesServer Instance
    {
        get
        {
            if (ServiceHost.IsInitialized)
                return (DevicesServer)ServiceHost.GetRequiredService<IDeviceServer>();
            Log.Error("[DevicesServer] Instance: ServiceHost not initialized! Returning orphan instance — " +
                "this indicates a DI initialization order bug. Use ServiceHost/constructor injection instead.");
            return new DevicesServer();
        }
    }

    private readonly Dictionary<DeviceLocator, string> _signedDeviceTokens = new();
    private IWebHost? _host;
    private int? _configuredPort;

    /// <summary>
    /// Whether device key exchange is in progress
    /// </summary>
    private bool _isExchangingDeviceKey = false;

    /// <summary>
    /// Device key exchange verification code
    /// </summary>
    private string? _exchangeDeviceKeyCode;

    /// <summary>
    /// TaskCompletionSource for awaiting user confirmation on key exchange
    /// </summary>
    private TaskCompletionSource<bool>? _exchangeKeyTcs;

    /// <summary>
    /// Pending exchange key request, stored for later processing after user confirms
    /// </summary>
    private ExchangeKeyRequest? _pendingExchangeRequest;

    /// <summary>
    /// Number of key exchange attempts in the current rate-limit window
    /// </summary>
    private int _exchangeAttempts;

    /// <summary>
    /// Start time of the current rate-limit window
    /// </summary>
    private DateTime _exchangeAttemptWindowStart = DateTime.MinValue;

    /// <summary>
    /// Maximum number of key exchange attempts allowed per rate-limit window
    /// </summary>
    private const int MaxExchangeAttemptsPerWindow = 5;

    /// <summary>
    /// Duration of the rate-limit window
    /// </summary>
    private static readonly TimeSpan ExchangeRateLimitWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Timeout for user confirmation of key exchange
    /// </summary>
    private static readonly TimeSpan ExchangeKeyConfirmationTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// JSON serializer options for network protocol (compatible with legacy KitX)
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Pending plugin invoke responses, keyed by RequestId, for correlating async responses
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingPluginResponses = new();

    /// <summary>
    /// Event raised when port changes
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<int>? PortChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Creates a new device server
    /// </summary>
    public DevicesServer() { }

    /// <summary>
    /// Gets or sets the port
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    /// Configures the port for the server
    /// </summary>
    /// <param name="port">The port number</param>
    public void ConfigurePort(int port)
    {
        _configuredPort = port is >= 0 and <= 65535 ? port : 8888;
    }

    /// <summary>
    /// Starts the device server
    /// </summary>
    /// <returns>The server instance</returns>
    public IDeviceServer Run()
    {
        if (!TryStart())
            return this;

        var port = _configuredPort ?? 8888;

        try
        {
            // Create and start the ASP.NET Core web host
            _host = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    // Add routing services
                    services.AddRouting();

                    // Add core services
                    services.AddSingleton(_signedDeviceTokens);
                })
                .UseKestrel()
                .UseUrls($"http://0.0.0.0:{port}")
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        // Basic health check endpoint
                        endpoints.MapGet("/", async context =>
                        {
                            await context.Response.WriteAsync("KitX DevicesServer is running");
                        });

                        // Device controller endpoints (旧架构 API 标准)
                        // GET /Api/V1/Device?token=xxx
                        // POST /Api/V1/Device/ExchangeKey?verifyCodeSHA1=xxx&address=xxx
                        // POST /Api/V1/Device/ExchangeKeyBack
                        // POST /Api/V1/Device/CancelExchangingKey
                        // POST /Api/V1/Device/Connect?deviceBase64=xxx
                        endpoints.MapGet("/Api/V1/Device", async context =>
                        {
                            await HandleGetDeviceInfoAsync(context);
                        });

                        endpoints.MapPost("/Api/V1/Device/{action}", async context =>
                        {
                            var action = context.Request.RouteValues["action"]?.ToString();
                            switch (action)
                            {
                                case "ExchangeKey":
                                    await HandleExchangeKeyAsync(context);
                                    break;
                                case "ExchangeKeyBack":
                                    await HandleExchangeKeyBackAsync(context);
                                    break;
                                case "CancelExchangingKey":
                                    await HandleCancelExchangingKeyAsync(context);
                                    break;
                                case "Connect":
                                    await HandleConnectAsync(context);
                                    break;
                                default:
                                    context.Response.StatusCode = 404;
                                    await context.Response.WriteAsync("Not found");
                                    break;
                            }
                        });

                        // Plugin controller endpoints (旧架构 API 标准)
                        // POST /Api/V1/Plugin/Invoke?token=xxx
                        endpoints.MapPost("/Api/V1/Plugin/{action}", async context =>
                        {
                            var action = context.Request.RouteValues["action"]?.ToString();
                            switch (action)
                            {
                                case "Invoke":
                                    await HandlePluginInvokeAsync(context);
                                    break;
                                default:
                                    context.Response.StatusCode = 404;
                                    await context.Response.WriteAsync("Not found");
                                    break;
                            }
                        });
                    });
                })
                .Build();

            // Start the host in a background thread
            var hostThread = new Thread(async () =>
            {
                try
                {
                    await _host.StartAsync();

                    // Get the actual port
                    var server = _host.Services.GetService<IServer>();
                    var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;

                    if (addresses is not null && addresses.Count > 0)
                    {
                        var uri = new Uri(addresses.First());
                        Port = uri.Port;

                        // Update ConstantTable with the actual port
                        ConstantTable.DevicesServerPort = Port ?? 0;

                        // Publish port changed event via EventService only (removed direct PortChanged event to avoid potential recursion)
                        EventService.Instance.Publish(EventNames.DevicesServerPortChanged, new PortChangedEventArgs { Port = Port ?? 0 });

                        Log.Information($"DevicesServer started on port {Port}");
                    }

                    SetRunning();
                }
                catch (Exception ex)
                {
                    SetErrored(ex, nameof(DevicesServer));
                }
            })
            {
                IsBackground = true
            };

            hostThread.Start();

            // Wait for server to start
            var timeout = 0;
            while (IsStarting && timeout < 50) // 5 seconds timeout
            {
                Thread.Sleep(100);
                timeout++;
            }

            if (!IsRunning)
            {
                Log.Warning("DevicesServer start timed out or failed");
            }
        }
        catch (Exception ex)
        {
            SetErrored(ex, nameof(DevicesServer));
        }

        return this;
    }

    /// <summary>
    /// Stops the device server
    /// </summary>
    public void Stop()
    {
        if (!TryStop())
            return;

        try
        {
            if (_host is not null)
            {
                _host.StopAsync().Wait(TimeSpan.FromSeconds(5));
                _host.Dispose();
                _host = null;
            }

            Log.Information("DevicesServer stopped");
            SetPending();
        }
        catch (Exception ex)
        {
            SetErrored(ex, nameof(DevicesServer));
        }
    }

    /// <summary>
    /// Closes the device server asynchronously (for backward compatibility)
    /// </summary>
    /// <returns>Task representing the asynchronous operation</returns>
    public async System.Threading.Tasks.Task CloseAsync()
    {
        Stop();
        await System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Checks if a device token exists
    /// </summary>
    /// <param name="token">The token to check</param>
    /// <returns>True if the token exists</returns>
    public bool IsDeviceTokenExist(string token) => _signedDeviceTokens.ContainsValue(token);

    /// <summary>
    /// Searches for a device by token
    /// </summary>
    /// <param name="token">The token to search for</param>
    /// <returns>The device locator or null if not found</returns>
    public DeviceLocator? SearchDeviceByToken(string token)
    {
        if (!IsDeviceTokenExist(token))
            return null;

        return _signedDeviceTokens.First(x => x.Value.Equals(token)).Key;
    }

    /// <summary>
    /// Checks if a device is signed in
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <returns>True if the device is signed in</returns>
    public bool IsDeviceSignedIn(DeviceLocator locator) => _signedDeviceTokens.ContainsKey(locator);

    /// <summary>
    /// Adds a device token
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <param name="token">The token</param>
    public void AddDeviceToken(DeviceLocator locator, string token) => _signedDeviceTokens.Add(locator, token);

    /// <summary>
    /// Signs in a device
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <returns>The generated token</returns>
    public string SignInDevice(DeviceLocator locator)
    {
        var token = Guid.NewGuid().ToString();

        while (_signedDeviceTokens.ContainsValue(token))
            token = Guid.NewGuid().ToString();

        if (!_signedDeviceTokens.TryAdd(locator, token))
            _signedDeviceTokens[locator] = token;

        Log.Information($"Device {locator} signed in with token {token}");

        return token;
    }

    /// <summary>
    /// Handles GetDeviceInfo request (旧架构 API)
    /// GET /Api/V1/Device?token=xxx
    /// </summary>
    private async System.Threading.Tasks.Task HandleGetDeviceInfoAsync(HttpContext context)
    {
        var token = context.Request.Query["token"].ToString();
        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing token parameter");
            return;
        }

        if (IsDeviceTokenExist(token))
        {
            var deviceInfo = DevicesDiscoveryServer.Instance?.DefaultDeviceInfo;
            if (deviceInfo != null)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(deviceInfo));
            }
            else
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Device info not available");
            }
        }
        else
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("You should connect to this device first.");
        }
    }

    /// <summary>
    /// Handles ExchangeKey request (旧架构 API)
    /// POST /Api/V1/Device/ExchangeKey?verifyCodeSHA1=xxx&address=xxx
    /// Requires user confirmation before accepting the key exchange.
    /// </summary>
    private async System.Threading.Tasks.Task HandleExchangeKeyAsync(HttpContext context)
    {
        try
        {
            // Rate limiting check
            var now = DateTime.UtcNow;
            if (now - _exchangeAttemptWindowStart > ExchangeRateLimitWindow)
            {
                _exchangeAttempts = 0;
                _exchangeAttemptWindowStart = now;
            }
            if (++_exchangeAttempts > MaxExchangeAttemptsPerWindow)
            {
                Log.Warning("[DevicesServer] Key exchange rate limit exceeded: {Attempts} attempts in window",
                    _exchangeAttempts);
                context.Response.StatusCode = 429;
                await context.Response.WriteAsync("Too many key exchange requests. Please try again later.");
                return;
            }

            if (_isExchangingDeviceKey)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Remote device is exchanging device key.");
                return;
            }

            var securityService = SecurityManager.Instance;
            if (securityService.LocalDeviceKey == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Remote device didn't set up device key.");
                return;
            }

            // Read request body
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<ExchangeKeyRequest>(body);

            if (request == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Invalid request");
                return;
            }

            if (request.DeviceKey is null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Device key is null");
                return;
            }

            // Generate verification code
            _exchangeDeviceKeyCode = Guid.NewGuid().ToString("N")[..8];
            _isExchangingDeviceKey = true;
            _pendingExchangeRequest = request;

            // Create TaskCompletionSource for user confirmation
            _exchangeKeyTcs = new TaskCompletionSource<bool>();

            // Publish event for UI to handle — requires user confirmation
            EventService.Instance.Publish(EventNames.OnReceiveExchangeDeviceKey,
                new ExchangeDeviceKeyEventArgs
                {
                    VerificationCode = _exchangeDeviceKeyCode,
                    RequestingDeviceAddress = request.Address ?? string.Empty,
                    EncryptedDeviceKey = request.DeviceKey
                });

            Log.Information("[DevicesServer] Key exchange request received, waiting for user confirmation. " +
                "Verification code: {Code}", _exchangeDeviceKeyCode);

            // Wait for user confirmation with timeout
            using var cts = new CancellationTokenSource(ExchangeKeyConfirmationTimeout);
            try
            {
                var accepted = await _exchangeKeyTcs.Task.WaitAsync(cts.Token);

                if (!accepted)
                {
                    Log.Information("[DevicesServer] Key exchange rejected by user");
                    _isExchangingDeviceKey = false;
                    _exchangeDeviceKeyCode = null;
                    _pendingExchangeRequest = null;
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Key exchange rejected by user");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                Log.Warning("[DevicesServer] Key exchange confirmation timed out after {Timeout}s",
                    ExchangeKeyConfirmationTimeout.TotalSeconds);
                _isExchangingDeviceKey = false;
                _exchangeDeviceKeyCode = null;
                _pendingExchangeRequest = null;
                context.Response.StatusCode = 408;
                await context.Response.WriteAsync("Key exchange confirmation timed out");
                return;
            }
            finally
            {
                _exchangeKeyTcs = null;
            }

            // User confirmed — proceed with key exchange
            var deviceKeyDecrypted = securityService.AesDecrypt(request.DeviceKey, _exchangeDeviceKeyCode);
            var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

            if (deviceKeyInstance == null)
            {
                _isExchangingDeviceKey = false;
                _exchangeDeviceKeyCode = null;
                _pendingExchangeRequest = null;
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Failed to decrypt device key");
                return;
            }

            // Add device key
            securityService.AddDeviceKey(
                deviceKeyInstance.Device.MacAddress,
                deviceKeyInstance.Device.DeviceName,
                deviceKeyInstance.RsaPublicKeyPem ?? ""
            );

            // Send back local key
            var currentKey = securityService.GetPrivateDeviceKey();
            if (currentKey == null)
            {
                _isExchangingDeviceKey = false;
                _exchangeDeviceKeyCode = null;
                _pendingExchangeRequest = null;
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Failed to get local key");
                return;
            }

            var currentKeyJson = JsonSerializer.Serialize(currentKey);
            var currentKeyEncrypted = securityService.AesEncrypt(currentKeyJson, _exchangeDeviceKeyCode!);

            _isExchangingDeviceKey = false;
            _exchangeDeviceKeyCode = null;
            _pendingExchangeRequest = null;

            // Publish accept event
            EventService.Instance.Publish(EventNames.OnAcceptingDeviceKey, EventArgs.Empty);

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(currentKeyEncrypted));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in HandleExchangeKeyAsync");
            _isExchangingDeviceKey = false;
            _exchangeDeviceKeyCode = null;
            _pendingExchangeRequest = null;
            _exchangeKeyTcs?.TrySetCanceled();
            _exchangeKeyTcs = null;
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles device key exchange back response
    /// </summary>
    private async System.Threading.Tasks.Task HandleExchangeKeyBackAsync(HttpContext context)
    {
        try
        {
            if (_exchangeDeviceKeyCode == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("No pending key exchange");
                return;
            }

            // Read request body
            using var reader = new StreamReader(context.Request.Body);
            var encryptedKey = await reader.ReadToEndAsync();

            var securityService = SecurityManager.Instance;
            var deviceKeyDecrypted = securityService.AesDecrypt(encryptedKey, _exchangeDeviceKeyCode);
            var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

            if (deviceKeyInstance == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Failed to decrypt device key");
                return;
            }

            // Add device key
            securityService.AddDeviceKey(
                deviceKeyInstance.Device.MacAddress,
                deviceKeyInstance.Device.DeviceName,
                deviceKeyInstance.RsaPublicKeyPem ?? ""
            );

            _exchangeDeviceKeyCode = null;

            // Publish accept event
            EventService.Instance.Publish(EventNames.OnAcceptingDeviceKey, EventArgs.Empty);

            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("OK");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in HandleExchangeKeyBackAsync");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles CancelExchangingKey request (旧架构 API)
    /// POST /Api/V1/Device/CancelExchangingKey
    /// </summary>
    private async System.Threading.Tasks.Task HandleCancelExchangingKeyAsync(HttpContext context)
    {
        if (_isExchangingDeviceKey == false)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Remote device isn't exchanging device key.");
            return;
        }

        EventService.Instance.Publish(EventNames.OnReceiveCancelExchangingDeviceKey, EventArgs.Empty);

        // Cancel any pending user confirmation
        _exchangeKeyTcs?.TrySetCanceled();
        _exchangeKeyTcs = null;

        _isExchangingDeviceKey = false;
        _exchangeDeviceKeyCode = null;
        _pendingExchangeRequest = null;

        context.Response.StatusCode = 200;
        await context.Response.WriteAsync("OK");
    }

    /// <summary>
    /// Accepts a pending key exchange request. Called by UI layer after user confirms.
    /// The verification code must match the one displayed to the user.
    /// </summary>
    /// <param name="verificationCode">The verification code displayed to the user</param>
    /// <returns>True if the exchange was accepted successfully, false if no pending exchange or code mismatch</returns>
    public bool AcceptExchangeKey(string verificationCode)
    {
        if (!_isExchangingDeviceKey || _exchangeKeyTcs == null)
            return false;

        // Verify the code matches to prevent unauthorized acceptance
        if (!string.Equals(verificationCode, _exchangeDeviceKeyCode, StringComparison.Ordinal))
        {
            Log.Warning("[DevicesServer] Key exchange acceptance failed: verification code mismatch");
            return false;
        }

        Log.Information("[DevicesServer] Key exchange accepted by user");
        _exchangeKeyTcs.TrySetResult(true);
        return true;
    }

    /// <summary>
    /// Rejects a pending key exchange request. Called by UI layer when user declines.
    /// </summary>
    public void RejectExchangeKey()
    {
        if (_exchangeKeyTcs == null)
            return;

        Log.Information("[DevicesServer] Key exchange rejected by user");
        _exchangeKeyTcs.TrySetResult(false);
    }

    /// <summary>
    /// Handles Connect request (旧架构 API)
    /// POST /Api/V1/Device/Connect?deviceBase64=xxx
    /// </summary>
    private async System.Threading.Tasks.Task HandleConnectAsync(HttpContext context)
    {
        try
        {
            var deviceBase64 = context.Request.Query["deviceBase64"].ToString();
            if (string.IsNullOrEmpty(deviceBase64))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync($"Missing deviceBase64 parameter");
                return;
            }

            // Read request body (encrypted device name)
            using var reader = new StreamReader(context.Request.Body);
            var deviceNameEncrypted = await reader.ReadToEndAsync();

            // Decode device locator
            var deviceBytes = Convert.FromBase64String(deviceBase64);
            var deviceJson = Encoding.UTF8.GetString(deviceBytes);
            var device = JsonSerializer.Deserialize<DeviceLocator>(deviceJson);

            if (device == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync($"Invalid deviceBase64 parameter");
                return;
            }

            // Search for device key
            var securityService = SecurityManager.Instance;
            var key = securityService.SearchDeviceKey(device);

            if (key == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("You are not authorized by remote device.");
                return;
            }

            // Decrypt and verify device name
            var deviceNameDecrypted = securityService.RsaDecryptString(key, deviceNameEncrypted);

            if (deviceNameDecrypted == null)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Remote crashed when decrypting device name.");
                return;
            }

            if (!device.DeviceName.Equals(deviceNameDecrypted))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("You provided incorrect encrypted device name.");
                return;
            }

            // Sign in device
            var token = SignInDevice(device);

            // Encrypt token with the requesting device's public key
            var encryptedToken = await securityService.EncryptStringAsync(token, device.MacAddress);

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(encryptedToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in HandleConnectAsync");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles Plugin/Invoke request — routes plugin command to local PluginsServer connection.
    /// Protocol compatible with legacy PluginController.Invoke.
    /// POST /Api/V1/Plugin/Invoke?token=xxx
    /// </summary>
    private async Task HandlePluginInvokeAsync(HttpContext context)
    {
        const string location = $"{nameof(DevicesServer)}.{nameof(HandlePluginInvokeAsync)}";

        try
        {
            // 1. Validate token
            var token = context.Request.Query["token"].ToString();
            if (string.IsNullOrEmpty(token))
            {
                Log.Warning("[{Location}] Missing token in Plugin/Invoke request", location);
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Missing token parameter");
                return;
            }

            if (!IsDeviceTokenExist(token))
            {
                Log.Warning("[{Location}] Invalid token in Plugin/Invoke request", location);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("You should connect to this device first.");
                return;
            }

            // 2. Read base64-wrapped request JSON from body
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrEmpty(body))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Missing request body");
                return;
            }

            string requestJson;
            try
            {
                // Legacy format: body is a JSON string containing base64(data)
                var wrapped = JsonSerializer.Deserialize<string>(body, SerializerOptions);
                if (string.IsNullOrEmpty(wrapped))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid request body format");
                    return;
                }
                requestJson = Encoding.UTF8.GetString(Convert.FromBase64String(wrapped));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[{Location}] Failed to decode request body", location);
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Invalid request body encoding");
                return;
            }

            // 3. Deserialize Request
            var request = JsonSerializer.Deserialize<Request>(requestJson, SerializerOptions);
            if (request == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Invalid request format");
                return;
            }

            // 4. Validate request.Target
            var senderLocator = SearchDeviceByToken(token);
            if (request.Target == null)
            {
                Log.Warning("[{Location}] Plugin/Invoke request has no target", location);
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Provide target field please.");
                return;
            }

            if (!request.Target.IsSameDevice(senderLocator ?? new DeviceLocator()))
            {
                Log.Warning("[{Location}] Plugin/Invoke request target mismatch: {Target} vs {Sender}",
                    location, request.Target, senderLocator);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Please send to actual target.");
                return;
            }

            // 5. Handle content decryption if encrypted (simplified — full encryption handled by SecurityManager)
            var content = request.Content;
            if (request.EncryptionInfo?.IsEncrypted == true)
            {
                content = DecryptContent(request, token);
            }

            // 6. Deserialize Command
            var command = JsonSerializer.Deserialize<Command>(content, SerializerOptions);
            if (command.Equals(default(Command)))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Invalid command format");
                return;
            }

            // 7. Find local plugin connection by PluginConnectionId
            var connector = PluginsServer.Instance.FindConnection(command.PluginConnectionId);
            if (connector == null)
            {
                Log.Warning("[{Location}] Plugin connection not found: {ConnectionId}",
                    location, command.PluginConnectionId);
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Plugin connection not found");
                return;
            }

            // 8. Generate RequestId and set up async response wait
            var requestId = Guid.NewGuid().ToString();
            command.Tags ??= new();
            command.Tags["RequestId"] = requestId;

            var tcs = new TaskCompletionSource<string>();

            // Subscribe to plugin response
            void OnResponse(object? sender, PluginResponseEventArgs e)
            {
                if (e.RequestId == requestId)
                {
                    PluginsServer.Instance.PluginResponse -= OnResponse;
                    _pendingPluginResponses.TryRemove(requestId, out _);
                    tcs.TrySetResult(e.Content);
                }
            }
            PluginsServer.Instance.PluginResponse += OnResponse;
            _pendingPluginResponses[requestId] = tcs;

            // 9. Build the request to send to plugin (manual copy since Request is class not record)
            var updatedRequest = new Request
            {
                Type = request.Type,
                Version = request.Version,
                Sender = request.Sender,
                Target = request.Target,
                EncryptionInfo = request.EncryptionInfo,
                CompressionInfo = request.CompressionInfo,
                Content = content
            };
            var pluginRequestJson = JsonSerializer.Serialize(updatedRequest, SerializerOptions);

            // 10. Send to plugin via local PluginsServer
            connector.Send(pluginRequestJson);

            Log.Information("[{Location}] Forwarded plugin invoke to {PluginId}, RequestId: {RequestId}",
                location, command.PluginConnectionId, requestId);

            // 11. Wait for response with 30s timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var result = await tcs.Task.WaitAsync(cts.Token);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(result);
                Log.Information("[{Location}] Plugin invoke completed, RequestId: {RequestId}", location, requestId);
            }
            catch (TimeoutException)
            {
                Log.Warning("[{Location}] Plugin invoke timed out, RequestId: {RequestId}", location, requestId);
                _pendingPluginResponses.TryRemove(requestId, out _);
                PluginsServer.Instance.PluginResponse -= OnResponse;
                context.Response.StatusCode = 504;
                await context.Response.WriteAsync("Plugin invocation timed out");
            }
            catch (OperationCanceledException)
            {
                Log.Warning("[{Location}] Plugin invoke cancelled, RequestId: {RequestId}", location, requestId);
                _pendingPluginResponses.TryRemove(requestId, out _);
                PluginsServer.Instance.PluginResponse -= OnResponse;
                context.Response.StatusCode = 499;
                await context.Response.WriteAsync("Plugin invocation cancelled");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Location}] Error handling Plugin/Invoke request", location);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Decrypts request content based on encryption method.
    /// Simplified implementation — full RSA/AES decryption delegated to SecurityManager.
    /// </summary>
    private string DecryptContent(Request request, string token)
    {
        if (request.EncryptionInfo == null || !request.EncryptionInfo.IsEncrypted)
            return request.Content;

        var content = request.Content;

        if (request.EncryptionInfo.EncryptionMethod == EncryptionMethods.RSA)
        {
            var device = SearchDeviceByToken(token);
            if (device != null)
            {
                var key = SecurityManager.Instance.SearchDeviceKey(device);
                if (key != null)
                {
                    try
                    {
                        var encryptedContent = JsonSerializer.Deserialize<EncryptedContent>(content, SerializerOptions);
                        if (encryptedContent != null)
                        {
                            content = SecurityManager.Instance.RsaDecryptContent(key, encryptedContent) ?? content;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[DevicesServer] Failed to RSA-decrypt plugin invoke content");
                    }
                }
            }
        }

        return content;
    }
}
