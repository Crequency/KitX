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
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Device server for HTTP API
/// Phase 5: Simplified implementation using direct WebHostBuilder
/// </summary>
public class DevicesServer : IDeviceServer
{
    private static DevicesServer? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static DevicesServer Instance => _instance ??= new();

    private readonly Dictionary<DeviceLocator, string> _signedDeviceTokens = new();
    private ServerStatus _status = ServerStatus.Pending;
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
    /// Gets the service status
    /// </summary>
    public ServerStatus Status => _status;

    /// <summary>
    /// Event raised when port changes
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<int>? PortChanged;
#pragma warning restore CS0067

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
        if (_status != ServerStatus.Pending)
            return this;

        _status = ServerStatus.Starting;

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

                    _status = ServerStatus.Running;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to start DevicesServer: {ex.Message}");
                    _status = ServerStatus.Errored;
                }
            })
            {
                IsBackground = true
            };

            hostThread.Start();

            // Wait for server to start
            var timeout = 0;
            while (_status == ServerStatus.Starting && timeout < 50) // 5 seconds timeout
            {
                Thread.Sleep(100);
                timeout++;
            }

            if (_status != ServerStatus.Running)
            {
                Log.Warning("DevicesServer start timed out or failed");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error starting DevicesServer: {ex.Message}");
            _status = ServerStatus.Errored;
        }

        return this;
    }

    /// <summary>
    /// Stops the device server
    /// </summary>
    public void Stop()
    {
        if (_status != ServerStatus.Running)
            return;

        _status = ServerStatus.Stopping;

        try
        {
            if (_host is not null)
            {
                _host.StopAsync().Wait(TimeSpan.FromSeconds(5));
                _host.Dispose();
                _host = null;
            }

            Log.Information("DevicesServer stopped");
            _status = ServerStatus.Pending;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error stopping DevicesServer: {ex.Message}");
            _status = ServerStatus.Errored;
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
    /// </summary>
    private async System.Threading.Tasks.Task HandleExchangeKeyAsync(HttpContext context)
    {
        try
        {
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

            // Generate verification code
            _exchangeDeviceKeyCode = Guid.NewGuid().ToString("N")[..8];
            _isExchangingDeviceKey = true;

            // Publish event for UI to handle
            EventService.Instance.Publish(EventNames.OnReceiveCancelExchangingDeviceKey, EventArgs.Empty);

            // For now, auto-accept the key exchange (in real implementation, this would show a UI)
            // TODO: Integrate with UI for verification code input
            if (request.DeviceKey is null)
            {
                _isExchangingDeviceKey = false;
                _exchangeDeviceKeyCode = null;
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Device key is null");
                return;
            }

            var deviceKeyDecrypted = securityService.AesDecrypt(request.DeviceKey, _exchangeDeviceKeyCode);
            var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

            if (deviceKeyInstance == null)
            {
                _isExchangingDeviceKey = false;
                _exchangeDeviceKeyCode = null;
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
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Failed to get local key");
                return;
            }

            var currentKeyJson = JsonSerializer.Serialize(currentKey);
            var currentKeyEncrypted = securityService.AesEncrypt(currentKeyJson, _exchangeDeviceKeyCode!);

            _isExchangingDeviceKey = false;
            _exchangeDeviceKeyCode = null;

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

        _isExchangingDeviceKey = false;
        _exchangeDeviceKeyCode = null;

        context.Response.StatusCode = 200;
        await context.Response.WriteAsync("OK");
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

            // Encrypt token with local private key
            var encryptedToken = securityService.EncryptStringAsync(token, "").Result;

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
    /// Handles legacy ExchangeKey request (旧架构兼容)
    /// </summary>
    private async System.Threading.Tasks.Task HandleExchangeKeyLegacyAsync(HttpContext context)
    {
        try
        {
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

            // Read query parameters (legacy format)
            var verifyCodeSHA1 = context.Request.Query["verifyCodeSHA1"].ToString();
            var address = context.Request.Query["address"].ToString();

            // Read body (device key as plain string)
            using var reader = new StreamReader(context.Request.Body);
            var deviceKey = await reader.ReadToEndAsync();

            // Generate verification code
            _exchangeDeviceKeyCode = Guid.NewGuid().ToString("N")[..8];
            _isExchangingDeviceKey = true;

            // Auto-accept for now (legacy would show UI)
            var deviceKeyDecrypted = securityService.AesDecrypt(deviceKey, _exchangeDeviceKeyCode);
            var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

            if (deviceKeyInstance == null)
            {
                _isExchangingDeviceKey = false;
                _exchangeDeviceKeyCode = null;
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
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Failed to get local key");
                return;
            }

            var currentKeyJson = JsonSerializer.Serialize(currentKey);
            var currentKeyEncrypted = securityService.AesEncrypt(currentKeyJson, _exchangeDeviceKeyCode);

            _isExchangingDeviceKey = false;
            _exchangeDeviceKeyCode = null;

            // Return as plain string (legacy format)
            await context.Response.WriteAsync(currentKeyEncrypted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in HandleExchangeKeyLegacyAsync");
            _isExchangingDeviceKey = false;
            _exchangeDeviceKeyCode = null;
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles legacy Connect request (旧架构兼容)
    /// </summary>
    private async System.Threading.Tasks.Task HandleConnectLegacyAsync(HttpContext context)
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

            // Read body (encrypted device name as plain string)
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

            // Encrypt token (legacy uses EncryptString)
            var encryptedToken = securityService.EncryptStringAsync(token, "").Result;

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(encryptedToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in HandleConnectLegacyAsync");
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

/// <summary>
/// Exchange key request model
/// </summary>
public class ExchangeKeyRequest
{
    /// <summary>
    /// AES encrypted device key
    /// </summary>
    public string? DeviceKey { get; set; }

    /// <summary>
    /// Address of requesting device
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// SHA1 of verification code
    /// </summary>
    public string? VerifyCodeSHA1 { get; set; }
}
