using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Contract.Security;
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
public class DevicesServer : ServerBase, IDeviceServer
{
    private readonly IEncryptionService _encryptionService;
    private readonly IDeviceKeyService _deviceKeyService;
    private readonly IEventService _eventService;
    private readonly IPluginServer _pluginServer;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    private readonly ConcurrentDictionary<DeviceLocator, string> _signedDeviceTokens = new();

    /// <summary>
    /// Reverse index token → device locator, so token lookups are O(1) and atomic.
    /// Kept in sync with <see cref="_signedDeviceTokens"/> under <see cref="_signedDeviceTokensLock"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, DeviceLocator> _tokenToLocator = new();

    /// <summary>
    /// Serializes multi-entry updates of the token maps (AddDeviceToken / SignInDevice).
    /// </summary>
    private readonly object _signedDeviceTokensLock = new();
    private IWebHost? _host;
    private int? _configuredPort;

    /// <summary>
    /// Whether device key exchange is in progress
    /// </summary>
    private bool _isExchangingDeviceKey = false;

    /// <summary>
    /// Password entered by the user on this device, read from the initiating device's screen.
    /// Used to decrypt the exchanged device key payload. The correctness of the password is
    /// verified by the decrypt attempt itself (per the encryption-ring spec), so no separate
    /// verification code is needed.
    /// </summary>
    private string? _exchangeKeyPassword;

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
    /// JSON serializer options for network protocol (compatible with legacy KitX).
    /// C-15.8: shared instance.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = KitX.Core.Configuration.NetworkSerialization.Options;

    /// <summary>
    /// Pending plugin invoke responses, keyed by RequestId, for correlating async responses
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingPluginResponses = new();

    /// <summary>
    /// Creates a new device server with all dependencies injected.
    /// </summary>
    /// <param name="encryptionService">Encryption service for cryptographic operations</param>
    /// <param name="deviceKeyService">Device key management service</param>
    /// <param name="eventService">Event service for publishing events</param>
    /// <param name="pluginServer">Plugin server for managing plugin connections</param>
    /// <param name="deviceDiscoveryService">Device discovery service</param>
    public DevicesServer(
        IEncryptionService encryptionService,
        IDeviceKeyService deviceKeyService,
        IEventService eventService,
        IPluginServer pluginServer,
        IDeviceDiscoveryService deviceDiscoveryService)
    {
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _deviceKeyService = deviceKeyService ?? throw new ArgumentNullException(nameof(deviceKeyService));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _pluginServer = pluginServer ?? throw new ArgumentNullException(nameof(pluginServer));
        _deviceDiscoveryService = deviceDiscoveryService ?? throw new ArgumentNullException(nameof(deviceDiscoveryService));
    }

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
        // C-15.12: invalid input clears the configured port so the fallback chain
        // (ConstantTable.DevicesServerPort -> 8888) applies — same policy as PluginsServer.
        _configuredPort = port is >= 0 and <= 65535 ? port : null;
    }

    /// <summary>
    /// Starts the device server
    /// </summary>
    /// <returns>The server instance</returns>
    public IDeviceServer Run()
    {
        if (!TryStart())
            return this;

        // C-15.12: unified fallback chain — explicit config wins, then the runtime port
        // recorded in ConstantTable, then the default 8888 (mirrors PluginsServer).
        // Note: 0 must be treated as "not configured" (NOT bound — port 0 = random port).
        var port = _configuredPort > 0
            ? _configuredPort.Value
            : ConstantTable.DevicesServerPort > 0 ? ConstantTable.DevicesServerPort : 8888;

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
                        // GET /Api/V1/Device (token in Authorization: Bearer header)
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
                        // POST /Api/V1/Plugin/Invoke (token in Authorization: Bearer header)
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
                        _eventService?.Publish(EventNames.DevicesServerPortChanged, new PortChangedEventArgs { Port = Port ?? 0 });

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
    public bool IsDeviceTokenExist(string token) => _tokenToLocator.ContainsKey(token);

    /// <summary>
    /// Searches for a device by token
    /// </summary>
    /// <param name="token">The token to search for</param>
    /// <returns>The device locator or null if not found</returns>
    public DeviceLocator? SearchDeviceByToken(string token)
    {
        return _tokenToLocator.TryGetValue(token, out var locator) ? locator : null;
    }

    /// <summary>
    /// Checks if a device is signed in
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <returns>True if the device is signed in</returns>
    public bool IsDeviceSignedIn(DeviceLocator locator) => _signedDeviceTokens.ContainsKey(locator);

    /// <summary>
    /// Gets the signed device token for a device locator
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <returns>The token or null if not found</returns>
    public string? GetDeviceToken(DeviceLocator locator) =>
        _signedDeviceTokens.TryGetValue(locator, out var token) ? token : null;

    /// <summary>
    /// Gets all signed-in device locators
    /// </summary>
    /// <returns>Read-only list of signed-in device locators</returns>
    public IReadOnlyList<DeviceLocator> GetSignedInDevices() =>
        _signedDeviceTokens.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Adds a device token.
    /// Internal: no caller currently exists — if a future feature needs to seed a token
    /// programmatically it must go through <see cref="SignInDevice"/> (which keeps the
    /// reverse index consistent). Exposing this publicly would allow unauthenticated
    /// token injection into the signed-in table.
    /// </summary>
    internal void AddDeviceToken(DeviceLocator locator, string token)
    {
        lock (_signedDeviceTokensLock)
        {
            _signedDeviceTokens[locator] = token;
            _tokenToLocator[token] = locator;
        }
    }

    /// <summary>
    /// Signs in a device
    /// </summary>
    /// <param name="locator">The device locator</param>
    /// <returns>The generated token</returns>
    public string SignInDevice(DeviceLocator locator)
    {
        var token = Guid.NewGuid().ToString();

        while (_tokenToLocator.ContainsKey(token))
            token = Guid.NewGuid().ToString();

        lock (_signedDeviceTokensLock)
        {
            // Re-check under the lock in case of a concurrent sign-in for the same device
            if (_signedDeviceTokens.TryGetValue(locator, out var existingToken) &&
                _tokenToLocator.TryGetValue(existingToken, out var existingLocator) &&
                existingLocator.Equals(locator))
            {
                // Device already signed in — return the existing token
                Log.Information("Device {Locator} already signed in", locator);
                return existingToken;
            }

            _signedDeviceTokens[locator] = token;
            _tokenToLocator[token] = locator;
        }

        Log.Information("Device {Locator} signed in", locator);

        return token;
    }

    /// <summary>
    /// Extracts the device token from an HTTP request. Preferred: the
    /// <c>Authorization: Bearer {token}</c> header (or the <c>X-Device-Token</c> header),
    /// so the token never appears in the URL. A legacy <c>?token=</c> query fallback is
    /// kept for older KitX clients that predate the header migration.
    /// </summary>
    private static string GetTokenFromRequest(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader["Bearer ".Length..].Trim();

        var deviceTokenHeader = context.Request.Headers["X-Device-Token"].ToString();
        if (!string.IsNullOrEmpty(deviceTokenHeader))
            return deviceTokenHeader.Trim();

        return context.Request.Query["token"].ToString();
    }

    /// <summary>
    /// Handles GetDeviceInfo request (旧架构 API)
    /// GET /Api/V1/Device
    /// </summary>
    private async System.Threading.Tasks.Task HandleGetDeviceInfoAsync(HttpContext context)
    {
        var token = GetTokenFromRequest(context);
        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing token parameter");
            return;
        }

        if (IsDeviceTokenExist(token))
        {
            var deviceInfo = _deviceDiscoveryService.DefaultDeviceInfo;
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
    /// POST /Api/V1/Device/ExchangeKey?verifyCodeSHA1=xxx&amp;address=xxx
    /// Requires user confirmation before accepting the key exchange. The temporary
    /// password the user enters is the AES key that decrypts the exchanged payload, so
    /// a wrong password is detected by the decrypt attempt itself and the user is
    /// re-prompted (per the encryption-ring spec), rather than a separate code being used.
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

            var securityService = _encryptionService;
            var deviceKeyService = _deviceKeyService;
            if (securityService == null)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Encryption service not available");
                return;
            }

            // LocalDeviceKey is on IDeviceKeyService, check via GetPrivateDeviceKey
            if (deviceKeyService.GetPrivateDeviceKey() == null)
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

            _isExchangingDeviceKey = true;
            _pendingExchangeRequest = request;

            // Loop waiting for the user to enter the temporary password. A wrong password
            // yields a decrypt failure, so we keep the exchange pending and re-prompt.
            DeviceKey? deviceKeyInstance = null;
            while (deviceKeyInstance is null)
            {
                _exchangeKeyPassword = null;
                _exchangeKeyTcs = new TaskCompletionSource<bool>();

                // Publish event for UI to handle — requires user confirmation
                _eventService.Publish(EventNames.OnReceiveExchangeDeviceKey,
                    new ExchangeDeviceKeyEventArgs
                    {
                        RequestingDeviceAddress = request.Address ?? string.Empty,
                        EncryptedDeviceKey = request.DeviceKey
                    });

                Log.Information("[DevicesServer] Key exchange request received, waiting for user confirmation");

                // Wait for user confirmation with timeout
                using var cts = new CancellationTokenSource(ExchangeKeyConfirmationTimeout);
                bool accepted;
                try
                {
                    accepted = await _exchangeKeyTcs.Task.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log.Warning("[DevicesServer] Key exchange confirmation timed out after {Timeout}s",
                        ExchangeKeyConfirmationTimeout.TotalSeconds);
                    CleanupExchangeState();
                    context.Response.StatusCode = 408;
                    await context.Response.WriteAsync("Key exchange confirmation timed out");
                    return;
                }
                finally
                {
                    _exchangeKeyTcs = null;
                }

                if (!accepted)
                {
                    Log.Information("[DevicesServer] Key exchange rejected by user");
                    CleanupExchangeState();
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("Key exchange rejected by user");
                    return;
                }

                if (string.IsNullOrEmpty(_exchangeKeyPassword))
                {
                    Log.Warning("[DevicesServer] Key exchange accepted without a password, aborting");
                    CleanupExchangeState();
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid exchange password");
                    return;
                }

                string deviceKeyDecrypted;
                try
                {
                    deviceKeyDecrypted = securityService.AesDecrypt(request.DeviceKey, _exchangeKeyPassword);
                }
                catch (CryptographicException)
                {
                    // Wrong password — keep the exchange pending and re-prompt.
                    Log.Warning("[DevicesServer] Key exchange decrypt failed (wrong password?), re-prompting");
                    continue;
                }

                deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

                // Only trust the public key — never trust any private key field from the remote
                if (deviceKeyInstance == null || string.IsNullOrEmpty(deviceKeyInstance.RsaPublicKeyPem))
                {
                    Log.Warning("[DevicesServer] Received device key with missing or invalid public key");
                    deviceKeyInstance = null;
                    continue;
                }
            }

            // Add device key
            deviceKeyService.AddDeviceKey(
                deviceKeyInstance.Device.MacAddress,
                deviceKeyInstance.Device.DeviceName,
                deviceKeyInstance.RsaPublicKeyPem
            );

            // Send back local key — public key only. The private key never leaves this device.
            // Use the local key's public key directly (GetPrivateDeviceKey now carries it),
            // rather than re-looking it up by locator — the locator lookup can transiently
            // miss when the local key was just (re)generated, causing a spurious 500.
            var currentKey = deviceKeyService.GetPrivateDeviceKey();
            if (currentKey == null)
            {
                CleanupExchangeState();
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Failed to get local key");
                return;
            }

            var currentPublicKey = currentKey.RsaPublicKeyPem;
            if (string.IsNullOrEmpty(currentPublicKey))
            {
                Log.Warning("[DevicesServer] Local public key not found, aborting exchange");
                CleanupExchangeState();
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Failed to get local key");
                return;
            }

            var publicKeyOnly = new DeviceKey
            {
                Device = currentKey.Device,
                RsaPublicKeyPem = currentPublicKey
            };
            var publicKeyJson = JsonSerializer.Serialize(publicKeyOnly);
            var publicKeyEncrypted = securityService.AesEncrypt(publicKeyJson, _exchangeKeyPassword);

            // Exchange complete — the initiating device already stored this device's key,
            // and this device just stored the initiator's key. Clear all exchange state.
            CleanupExchangeState();

            // Publish accept event
            _eventService.Publish(EventNames.OnAcceptingDeviceKey, EventArgs.Empty);

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(publicKeyEncrypted));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in HandleExchangeKeyAsync");
            CleanupExchangeState();
            _exchangeKeyTcs?.TrySetCanceled();
            _exchangeKeyTcs = null;
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Failed to exchange device key. Please try again.");
        }
    }

    /// <summary>
    /// Handles device key exchange back response
    /// </summary>
    private async System.Threading.Tasks.Task HandleExchangeKeyBackAsync(HttpContext context)
    {
        try
        {
            if (_exchangeKeyPassword == null)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("No pending key exchange");
                return;
            }

            // Read request body
            using var reader = new StreamReader(context.Request.Body);
            var encryptedKey = await reader.ReadToEndAsync();

            var securityService = _encryptionService;
            var deviceKeyService = _deviceKeyService;
            var deviceKeyDecrypted = securityService.AesDecrypt(encryptedKey, _exchangeKeyPassword);
            var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

            // Only trust the public key — never trust any private key field from the remote
            if (deviceKeyInstance == null || string.IsNullOrEmpty(deviceKeyInstance.RsaPublicKeyPem))
            {
                Log.Warning("[DevicesServer] Received device key with missing or invalid public key");
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Failed to decrypt device key");
                return;
            }

            // Add device key
            deviceKeyService.AddDeviceKey(
                deviceKeyInstance.Device.MacAddress,
                deviceKeyInstance.Device.DeviceName,
                deviceKeyInstance.RsaPublicKeyPem
            );

            // Exchange complete — clear all exchange state
            CleanupExchangeState();

            // Publish accept event
            _eventService.Publish(EventNames.OnAcceptingDeviceKey, EventArgs.Empty);

            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("OK");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in HandleExchangeKeyBackAsync");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Failed to complete key exchange. Please try again.");
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

        _eventService.Publish(EventNames.OnReceiveCancelExchangingDeviceKey, EventArgs.Empty);

        // Cancel any pending user confirmation
        _exchangeKeyTcs?.TrySetCanceled();
        _exchangeKeyTcs = null;

        CleanupExchangeState();

        context.Response.StatusCode = 200;
        await context.Response.WriteAsync("OK");
    }

    /// <summary>
    /// Accepts a pending key exchange request. Called by UI layer after user confirms.
    /// The password (read by the user from the initiating device's screen) is the
    /// symmetric key used to decrypt the exchanged payload. Correctness is verified by
    /// the decrypt attempt itself — a wrong password yields a decrypt failure and a
    /// re-prompt (per the encryption ring spec).
    /// </summary>
    /// <param name="password">The temporary password entered by the user, read from the initiating device's screen</param>
    /// <returns>True if the exchange was accepted successfully, false if no pending exchange</returns>
    public bool AcceptExchangeKey(string password)
    {
        if (!_isExchangingDeviceKey || _exchangeKeyTcs == null)
            return false;

        // The password must be provided — it is the decryption key. Its correctness
        // is checked by the decrypt attempt after confirmation (per the encryption
        // ring spec: "verify decryption success, prompt again on failure").
        if (string.IsNullOrEmpty(password))
        {
            Log.Warning("[DevicesServer] Key exchange acceptance failed: empty password");
            return false;
        }

        _exchangeKeyPassword = password;

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
    /// Clears the in-progress device key exchange state.
    /// </summary>
    private void CleanupExchangeState()
    {
        _isExchangingDeviceKey = false;
        _exchangeKeyPassword = null;
        _pendingExchangeRequest = null;
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

            // Read request body (RSA signature over the requesting device's name,
            // produced with its private key — verified against its stored public key below)
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

            // Search for device key (the requesting device's stored public key)
            var securityService = _encryptionService;
            var deviceKeyService = _deviceKeyService;
            var key = deviceKeyService.SearchDeviceKey(device);

            if (key == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("You are not authorized by remote device.");
                return;
            }

            // Verify the requesting device's RSA signature over its device name using its
            // stored PUBLIC key. This proves the requester holds the private key exchanged
            // earlier (PKI) — a forged or unpaired device cannot produce a valid signature.
            var validSignature = securityService.RsaVerifySignature(key, device.DeviceName, deviceNameEncrypted);

            if (!validSignature)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("You provided an incorrect device signature.");
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
            await context.Response.WriteAsync("Failed to connect. Please try again.");
        }
    }

    /// <summary>
    /// Handles Plugin/Invoke request — routes plugin command to local PluginsServer connection.
    /// Protocol compatible with legacy PluginController.Invoke.
    /// POST /Api/V1/Plugin/Invoke (token in Authorization: Bearer header)
    /// </summary>
    private async Task HandlePluginInvokeAsync(HttpContext context)
    {
        const string location = $"{nameof(DevicesServer)}.{nameof(HandlePluginInvokeAsync)}";

        try
        {
            // 1. Validate token
            var token = GetTokenFromRequest(context);
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
            // C-15.9: Command is a struct — Equals(default(Command)) treated the empty
            // object {} (deserialized from "null"/"{}" content) as invalid, which is
            // correct, but a populated object with missing fields was indistinguishable.
            // Validate the fields this handler actually consumes instead.
            if (string.IsNullOrEmpty(command.PluginConnectionId) || string.IsNullOrEmpty(command.FunctionName))
            {
                Log.Warning("[{Location}] Command missing required fields " +
                    "(PluginConnectionId='{PluginConnectionId}', FunctionName='{FunctionName}')",
                    location, command.PluginConnectionId, command.FunctionName);
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Invalid command format");
                return;
            }

            // 7. Find local plugin connection by PluginConnectionId
            var connector = _pluginServer.FindConnection(command.PluginConnectionId);
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
                    _pluginServer.PluginResponse -= OnResponse;
                    _pendingPluginResponses.TryRemove(requestId, out _);
                    tcs.TrySetResult(e.Content);
                }
            }
            _pluginServer.PluginResponse += OnResponse;
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
                _pluginServer.PluginResponse -= OnResponse;
                context.Response.StatusCode = 504;
                await context.Response.WriteAsync("Plugin invocation timed out");
            }
            catch (OperationCanceledException)
            {
                Log.Warning("[{Location}] Plugin invoke cancelled, RequestId: {RequestId}", location, requestId);
                _pendingPluginResponses.TryRemove(requestId, out _);
                _pluginServer.PluginResponse -= OnResponse;
                context.Response.StatusCode = 499;
                await context.Response.WriteAsync("Plugin invocation cancelled");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{Location}] Error handling Plugin/Invoke request", location);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Failed to invoke plugin. Please try again.");
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
                var key = _deviceKeyService.SearchDeviceKey(device);
                if (key != null)
                {
                    try
                    {
                        var encryptedContent = JsonSerializer.Deserialize<EncryptedContent>(content, SerializerOptions);
                        if (encryptedContent != null)
                        {
                            content = _encryptionService.RsaDecryptContent(key, encryptedContent) ?? content;
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
