using KitX.Core.Contract.Configuration;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;

namespace KitX.Core.Plugin;

/// <summary>
/// Plugin installation implementation
/// </summary>
public class PluginInstallation : IPluginInstallation
{
    /// <summary>
    /// Gets the unique identifier for this plugin installation
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets the installation path
    /// </summary>
    public string? InstallPath { get; set; }

    /// <summary>
    /// Gets or sets the plugin information
    /// </summary>
    public PluginInfo? PluginInfo { get; set; }

    /// <summary>
    /// Gets or sets the loader information
    /// </summary>
    public LoaderInfo? LoaderInfo { get; set; }

    private List<DeviceLocator> _installedDevices = new();

    /// <summary>
    /// Gets or sets the list of installed devices
    /// </summary>
    public IList<DeviceLocator> InstalledDevices
    {
        get => _installedDevices;
        set => _installedDevices = new List<DeviceLocator>(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin is running
    /// </summary>
    public bool IsRunning { get; set; }
}