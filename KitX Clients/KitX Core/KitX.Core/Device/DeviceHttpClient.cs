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
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(35)
    };

    /// <summary>
    /// JSON serializer options (compatible with legacy KitX network protocol)
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

            // Step 3: Build URL
            var url = $"http://{ipv4}:{port}/Api/V1/Plugin/Invoke?token={token}";

            Log.Debug("[DeviceHttpClient] Sending plugin invoke to {Url}, Target={Target}, Function={Function}",
                url, request.Target, request.Content);

            // Step 4: Send HTTP POST
            var response = await _httpClient.PostAsync(
                url,
                new StringContent(wrappedJson, Encoding.UTF8, "application/json"),
                ct);

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
