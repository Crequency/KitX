using System.Text.Json.Serialization;
using KitX.Core.Contract.Configuration;
using KitX.Core.Plugin;

namespace KitX.Core.Configuration;

/// <summary>
/// Plugins configuration implementation
/// </summary>
public class PluginsConfig : IPluginsConfig, IConfigWithMetadata
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
    /// Plugins list (concrete type for proper serialization)
    /// </summary>
    public List<PluginInstallation> Plugins { get; set; } = [];

    /// <summary>
    /// Gets plugins as interface (for external use)
    /// </summary>
    [JsonIgnore]
    IList<IPluginInstallation> IPluginsConfig.Plugins
    {
        get => Plugins.Cast<IPluginInstallation>().ToList();
        set => Plugins = value?.Cast<PluginInstallation>().ToList() ?? [];
    }
}
