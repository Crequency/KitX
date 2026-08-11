using System.Net.Http;
using System.Text;
using System.Text.Json;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Security;
using KitX.Shared.CSharp.Device;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Outbound device encrypted-authentication connection client.
/// Initiates key exchange and connection against a remote <c>DevicesServer</c>,
/// mirroring the receive-side endpoints implemented in <see cref="DevicesServer"/>.
/// Only public keys are ever sent to the remote; the local private key never leaves
/// this process.
/// </summary>
public class DeviceConnectionClient : IDeviceConnectionClient
{
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 8,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        // Generous timeout: an exchange waits on the remote user's confirmation for up
        // to 60 seconds (ExchangeKeyConfirmationTimeout) before it responds.
        Timeout = TimeSpan.FromSeconds(75)
    };

    private readonly IDeviceKeyService _deviceKeyService;
    private readonly IEncryptionService _encryptionService;

    /// <summary>
    /// JSON serializer options (compatible with legacy KitX network protocol).
    /// C-15.8: shared instance.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = KitX.Core.Configuration.NetworkSerialization.Options;

    public DeviceConnectionClient(IDeviceKeyService deviceKeyService, IEncryptionService encryptionService)
    {
        _deviceKeyService = deviceKeyService ?? throw new System.ArgumentNullException(nameof(deviceKeyService));
        _encryptionService = encryptionService ?? throw new System.ArgumentNullException(nameof(encryptionService));
    }

    /// <inheritdoc/>
    public async Task<ExchangeKeyResult> ExchangeKeyAsync(
        DeviceInfo targetDevice,
        string password,
        CancellationToken ct = default)
    {
        if (targetDevice?.Device == null || !TryGetAddress(targetDevice, out var ipv4, out var port))
            return Fail("Invalid target device");

        try
        {
            var publicKeyJson = BuildLocalPublicKeyJson();
            var encryptedKey = _encryptionService.AesEncrypt(publicKeyJson, password);

            var request = new ExchangeKeyRequest
            {
                DeviceKey = encryptedKey,
                Address = GetLocalIpv4()
            };
            var body = JsonSerializer.Serialize(request, SerializerOptions);

            var url = $"http://{ipv4}:{port}/Api/V1/Device/ExchangeKey";
            Log.Information("[DeviceConnectionClient] Exchanging key with {Url}", url);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                Log.Warning("[DeviceConnectionClient] ExchangeKey failed: {Status} {Error}",
                    (int)response.StatusCode, err);
                return Fail($"ExchangeKey failed: {(int)response.StatusCode} {err}");
            }

            var encryptedRemoteKey = await response.Content.ReadAsStringAsync(ct);

            // The server wraps the Base64 in a JSON string literal ("..."), so unwrap it.
            var encryptedRemoteKeyBase64 = JsonSerializer.Deserialize<string>(encryptedRemoteKey, SerializerOptions);
            if (string.IsNullOrEmpty(encryptedRemoteKeyBase64))
            {
                Log.Warning("[DeviceConnectionClient] ExchangeKey returned an empty remote key payload");
                return Fail("Remote device returned an invalid public key");
            }

            var remoteKeyJson = _encryptionService.AesDecrypt(encryptedRemoteKeyBase64, password);
            var remoteKey = JsonSerializer.Deserialize<DeviceKey>(remoteKeyJson, SerializerOptions);

            if (remoteKey == null || string.IsNullOrEmpty(remoteKey.RsaPublicKeyPem))
            {
                Log.Warning("[DeviceConnectionClient] ExchangeKey returned an invalid remote key");
                return Fail("Remote device returned an invalid public key");
            }

            _deviceKeyService.AddDeviceKey(
                remoteKey.Device.MacAddress,
                remoteKey.Device.DeviceName,
                remoteKey.RsaPublicKeyPem);

            Log.Information("[DeviceConnectionClient] Key exchange complete with {Device}", remoteKey.Device.DeviceName);
            return new ExchangeKeyResult { Success = true, RemoteDeviceKey = remoteKey };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DeviceConnectionClient] ExchangeKeyAsync failed");
            return Fail(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExchangeKeyBackAsync(
        DeviceInfo targetDevice,
        string password,
        CancellationToken ct = default)
    {
        if (targetDevice?.Device == null || !TryGetAddress(targetDevice, out var ipv4, out var port))
            return false;

        try
        {
            var publicKeyJson = BuildLocalPublicKeyJson();
            var encryptedKey = _encryptionService.AesEncrypt(publicKeyJson, password);
            var url = $"http://{ipv4}:{port}/Api/V1/Device/ExchangeKeyBack";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(encryptedKey, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DeviceConnectionClient] ExchangeKeyBackAsync failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CancelExchangingKeyAsync(DeviceInfo targetDevice, CancellationToken ct = default)
    {
        if (targetDevice?.Device == null || !TryGetAddress(targetDevice, out var ipv4, out var port))
            return false;

        try
        {
            var url = $"http://{ipv4}:{port}/Api/V1/Device/CancelExchangingKey";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(httpRequest, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DeviceConnectionClient] CancelExchangingKeyAsync failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> ConnectAsync(DeviceInfo targetDevice, CancellationToken ct = default)
    {
        if (targetDevice?.Device == null || !TryGetAddress(targetDevice, out var ipv4, out var port))
            return null;

        try
        {
            var localKey = _deviceKeyService.GetPrivateDeviceKey();
            if (localKey == null)
                return null;

            // The target must already have our public key (from a completed exchange).
            var targetKey = _deviceKeyService.SearchDeviceKey(targetDevice.Device);
            if (targetKey == null || string.IsNullOrEmpty(targetKey.RsaPublicKeyPem))
            {
                Log.Warning("[DeviceConnectionClient] No public key for target {Device}; exchange first",
                    targetDevice.Device.DeviceName);
                return null;
            }

            // Sign our device name with our PRIVATE key and send our OWN locator. The remote
            // verifies the signature against the public key it stored during the exchange,
            // proving we hold the private key (PKI) before issuing a session token.
            var signature = _encryptionService.RsaSignString(localKey, localKey.Device.DeviceName);
            if (string.IsNullOrEmpty(signature))
                return null;

            var locatorJson = JsonSerializer.Serialize(localKey.Device, SerializerOptions);
            var deviceBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(locatorJson));
            var url = $"http://{ipv4}:{port}/Api/V1/Device/Connect?deviceBase64={Uri.EscapeDataString(deviceBase64)}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(signature, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                Log.Warning("[DeviceConnectionClient] Connect failed: {Status} {Error}", (int)response.StatusCode, err);
                return null;
            }

            var encryptedToken = await response.Content.ReadAsStringAsync(ct);
            var token = await _encryptionService.DecryptStringAsync(encryptedToken, localKey.Device.MacAddress);
            Log.Information("[DeviceConnectionClient] Connected to {Device}", targetDevice.Device.DeviceName);
            return token;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DeviceConnectionClient] ConnectAsync failed");
            return null;
        }
    }

    /// <summary>
    /// Serializes the local public-key-only <see cref="DeviceKey"/> (never the private key).
    /// </summary>
    private string BuildLocalPublicKeyJson()
    {
        var localKey = _deviceKeyService.GetPrivateDeviceKey()
            ?? throw new InvalidOperationException("Local device key not set up");

        var publicKeyPem = _deviceKeyService.SearchDeviceKey(localKey.Device)?.RsaPublicKeyPem;
        if (string.IsNullOrEmpty(publicKeyPem))
            throw new InvalidOperationException("Local public key not found");

        var publicOnlyKey = new DeviceKey
        {
            Device = localKey.Device,
            RsaPublicKeyPem = publicKeyPem
        };
        return JsonSerializer.Serialize(publicOnlyKey, SerializerOptions);
    }

    private string GetLocalIpv4()
    {
        try
        {
            return NetworkHelper.GetInterNetworkIPv4() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryGetAddress(DeviceInfo target, out string ipv4, out int port)
    {
        ipv4 = target.Device.IPv4;
        port = target.DevicesServerPort;
        return !string.IsNullOrEmpty(ipv4) && port > 0;
    }

    private static ExchangeKeyResult Fail(string error) =>
        new() { Success = false, Error = error };
}
