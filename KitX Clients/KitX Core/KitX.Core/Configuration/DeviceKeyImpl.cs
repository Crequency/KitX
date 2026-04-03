using System;
using System.Text.Json.Serialization;
using KitX.Core.Contract.Configuration;
using KitX.Shared.CSharp.Device;

namespace KitX.Core.Configuration;

/// <summary>
/// Device key implementation for JSON serialization
/// </summary>
public class DeviceKeyImpl : IDeviceKey
{
    /// <summary>
    /// Device locator
    /// </summary>
    [JsonPropertyName("Device")]
    public DeviceLocator Device { get; set; } = new();

    /// <summary>
    /// RSA public key in PEM format
    /// </summary>
    [JsonPropertyName("RsaPublicKeyPem")]
    public string? RsaPublicKeyPem { get; set; }

    /// <summary>
    /// RSA private key in PEM format
    /// </summary>
    [JsonPropertyName("RsaPrivateKeyPem")]
    public string? RsaPrivateKeyPem { get; set; }

    /// <summary>
    /// Time when this key was added
    /// </summary>
    [JsonPropertyName("AddedAt")]
    public DateTime AddedAt { get; set; } = DateTime.Now;

    // Explicit interface implementations - these won't be serialized since they use different names
    string IDeviceKey.MacAddress => Device?.MacAddress ?? string.Empty;
    string IDeviceKey.DeviceName => Device?.DeviceName ?? string.Empty;
    string IDeviceKey.PublicKey => RsaPublicKeyPem ?? string.Empty;
    DateTime IDeviceKey.AddedAt => AddedAt;
}
