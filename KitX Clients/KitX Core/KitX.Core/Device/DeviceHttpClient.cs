using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KitX.Core.Contract.Device;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.WebCommand;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Device HTTP client implementation.
/// Sends plugin invoke requests to remote DevicesServer over HTTP.
/// Protocol compatible with legacy PluginControllerExtensions.RemoteInvoke.
/// Implements the <see cref="IDeviceHttpClient"/> contract now defined in KitX.Core.Contract.
/// </summary>
public class DeviceHttpClient : IDeviceHttpClient
{
    // C-15.3: cap per-server connections so fan-out plugin invokes to one device
    // cannot exhaust the connection pool.
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 8,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(35)
    };

    /// <summary>
    /// JSON serializer options (compatible with legacy KitX network protocol).
    /// C-15.8: shared instance.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = KitX.Core.Configuration.NetworkSerialization.Options;

    /// <summary>
    /// Invokes a plugin method on a remote device.
    /// </summary>
    public async Task<HttpResponseMessage?> InvokePluginAsync(
        DeviceInfo targetDevice,
        string token,
        Request request,
        CancellationToken ct = default)
    {
        if (targetDevice?.Device == null)
        {
            Log.Warning("[DeviceHttpClient] InvokePluginAsync called with null targetDevice");
            return null;
        }

        var ipv4 = targetDevice.Device.IPv4;
        var port = targetDevice.DevicesServerPort;

        if (string.IsNullOrEmpty(ipv4) || port <= 0)
        {
            Log.Warning("[DeviceHttpClient] Invalid device address: IPv4={IPv4}, Port={Port}",
                ipv4, port);
            return null;
        }

        try
        {
            // Step 1: Serialize Request to JSON
            var requestJson = JsonSerializer.Serialize(request, SerializerOptions);

            // Step 2: Wrap in base64 (legacy protocol format)
            var requestJsonBytes = Encoding.UTF8.GetBytes(requestJson);
            var requestJsonBase64 = Convert.ToBase64String(requestJsonBytes);
            var wrappedJson = JsonSerializer.Serialize(requestJsonBase64);

            // Step 3: Build URL — the token is deliberately NOT placed in the URL query
            // (tokens in URLs leak via logs/history). It travels in the Authorization
            // header as a bearer credential.
            var url = $"http://{ipv4}:{port}/Api/V1/Plugin/Invoke";

            Log.Debug("[DeviceHttpClient] Sending plugin invoke to {Url}, Target={Target}, Function={Function}",
                url, request.Target, request.Content);

            // Step 4: Send HTTP POST with the token in the header
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(wrappedJson, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            httpRequest.Headers.TryAddWithoutValidation("X-Device-Token", token);

            var response = await _httpClient.SendAsync(httpRequest, ct);

            Log.Debug("[DeviceHttpClient] Received response from {Url}: Status={Status}",
                url, response.StatusCode);

            return response;
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "[DeviceHttpClient] HTTP error invoking plugin on device {Device}:{Port}",
                ipv4, port);
            return null;
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken != ct)
        {
            Log.Error(ex, "[DeviceHttpClient] Timeout invoking plugin on device {Device}:{Port}",
                ipv4, port);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DeviceHttpClient] Unexpected error invoking plugin on device {Device}:{Port}",
                ipv4, port);
            return null;
        }
    }
}
