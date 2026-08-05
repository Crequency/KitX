using System.Text.Json.Serialization;
using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Security configuration implementation
/// </summary>
public class SecurityConfig : ISecurityConf, IConfigWithMetadata
{
    /// <summary>
    /// Configuration file location
    /// </summary>
    public string? ConfigFileLocation { get; set; }

    /// <summary>
    /// Configuration file watcher name
    /// </summary>
    public string? ConfigFileWatcherName { get; set; }

    /// <summary>
    /// Configuration generated time
    /// </summary>
    public DateTime? ConfigGeneratedTime { get; set; } = DateTime.Now;

    /// <summary>
    /// Gets or sets the device keys list (concrete type for proper serialization)
    /// </summary>
    public List<DeviceKeyImpl> DeviceKeys { get; set; } = [];

    /// <summary>
    /// Gets the device keys as interface (for external use)
    /// </summary>
    [JsonIgnore]
    IList<IDeviceKey> ISecurityConf.DeviceKeys
    {
        get => DeviceKeys.Cast<IDeviceKey>().ToList();
        set => DeviceKeys = value?.Cast<DeviceKeyImpl>().ToList() ?? [];
    }
}
