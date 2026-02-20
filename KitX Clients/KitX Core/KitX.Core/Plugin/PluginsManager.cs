using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Plugin;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;
using Serilog;

namespace KitX.Core.Plugin;

/// <summary>
/// Plugin manager for managing plugin installations and lifecycle
/// </summary>
public class PluginsManager : IPluginService
{
    private static PluginsManager? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static PluginsManager Instance => _instance ??= new();

    private readonly List<PluginInstallation> _plugins = new();

    /// <summary>
    /// Event raised when plugin status changes
    /// </summary>
    public event EventHandler<PluginStatusChangedEventArgs>? PluginStatusChanged;

    /// <summary>
    /// Private constructor
    /// </summary>
    private PluginsManager() { }

    /// <summary>
    /// Gets all installed plugins (alias for GetInstalledPlugins)
    /// </summary>
    public IReadOnlyList<IPluginInstallation> Plugins => _plugins.ToList();

    /// <summary>
    /// Imports a plugin (synchronous version for backward compatibility)
    /// </summary>
    /// <param name="kxpFilePath">Path to the plugin file</param>
    /// <returns>True if import succeeded</returns>
    public bool ImportPlugin(string kxpFilePath)
    {
        return ImportPluginAsync(kxpFilePath).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Imports plugins (synchronous version for backward compatibility)
    /// </summary>
    /// <param name="kxpFilePaths">Paths to the plugin files</param>
    /// <returns>True if import succeeded</returns>
    public bool ImportPlugin(IEnumerable<string> kxpFilePaths)
    {
        var result = true;
        foreach (var path in kxpFilePaths)
        {
            result &= ImportPlugin(path);
        }
        return result;
    }

    /// <summary>
    /// Gets all installed plugins
    /// </summary>
    /// <returns>List of plugin installations</returns>
    public IReadOnlyList<IPluginInstallation> GetInstalledPlugins()
    {
        return _plugins.ToList();
    }

    /// <summary>
    /// Gets a plugin by its ID
    /// </summary>
    /// <param name="pluginId">The plugin ID</param>
    /// <returns>The plugin installation or null if not found</returns>
    public IPluginInstallation? GetPlugin(Guid pluginId)
    {
        return _plugins.FirstOrDefault(p => GeneratePluginId(p.PluginInfo!) == pluginId);
    }

    /// <summary>
    /// Generates a plugin ID from plugin info
    /// </summary>
    /// <param name="pluginInfo">The plugin info</param>
    /// <returns>The generated plugin ID</returns>
    private Guid GeneratePluginId(PluginInfo pluginInfo)
    {
        // Generate deterministic GUID from: PublisherName_AuthorName_Name_Version
        var input = $"{pluginInfo.PublisherName}_{pluginInfo.AuthorName}_{pluginInfo.Name}_{pluginInfo.Version}";

        // Use MD5 hash to create a deterministic GUID
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));

        // Convert first 16 bytes to GUID
        return new Guid(hash.Take(16).ToArray());
    }

    /// <summary>
    /// Imports a plugin package (.kxp file)
    /// </summary>
    /// <param name="kxpFilePath">Path to the .kxp file</param>
    /// <returns>True if import was successful</returns>
    public async Task<bool> ImportPluginAsync(string kxpFilePath)
    {
        const string location = $"{nameof(PluginsManager)}.{nameof(ImportPluginAsync)}";

        try
        {
            if (!File.Exists(kxpFilePath))
            {
                Log.Error($"Plugin file not found: {kxpFilePath}");
                return false;
            }

            // TODO: Implement actual KXP file decoding
            // For now, we'll create a placeholder installation
            var pluginInfo = new PluginInfo
            {
                Name = Path.GetFileNameWithoutExtension(kxpFilePath),
                Version = "1.0.0",
                PublisherName = "Unknown",
                AuthorName = "Unknown"
            };

            var installation = new PluginInstallation
            {
                InstallPath = Path.GetDirectoryName(kxpFilePath) ?? "./Plugins/",
                PluginInfo = pluginInfo,
                LoaderInfo = new LoaderInfo(),
                InstalledDevices = new List<DeviceLocator>()
            };

            var generatedId = GeneratePluginId(pluginInfo);
            _plugins.Add(installation);

            Log.Information($"Imported plugin: {pluginInfo.Name} (v{pluginInfo.Version})");

            // Raise plugin status changed event
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
            {
                PluginId = generatedId,
                PluginName = pluginInfo.Name,
                OldStatus = PluginStatus.Unknown,
                NewStatus = PluginStatus.Installed
            });

            return await System.Threading.Tasks.Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error importing plugin {kxpFilePath}: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult(false);
        }
    }

    /// <summary>
    /// Removes a plugin
    /// </summary>
    /// <param name="pluginId">The plugin ID</param>
    /// <returns>True if removal was successful</returns>
    public async Task<bool> RemovePluginAsync(Guid pluginId)
    {
        const string location = $"{nameof(PluginsManager)}.{nameof(RemovePluginAsync)}";

        try
        {
            var plugin = _plugins.FirstOrDefault(p => GeneratePluginId(p.PluginInfo!) == pluginId);

            if (plugin == null)
            {
                Log.Warning($"Plugin not found: {pluginId}");
                return await System.Threading.Tasks.Task.FromResult(false);
            }

            // Remove from list
            _plugins.Remove(plugin);

            // TODO: Delete plugin files if needed
            if (Directory.Exists(plugin.InstallPath))
            {
                try
                {
                    Directory.Delete(plugin.InstallPath, true);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, $"Failed to delete plugin directory: {plugin.InstallPath}");
                }
            }

            Log.Information($"Removed plugin: {plugin.PluginInfo?.Name}");

            // Raise plugin status changed event
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
            {
                PluginId = pluginId,
                PluginName = plugin.PluginInfo?.Name ?? "Unknown",
                OldStatus = PluginStatus.Installed,
                NewStatus = PluginStatus.Stopped
            });

            return await System.Threading.Tasks.Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error removing plugin {pluginId}: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult(false);
        }
    }

    /// <summary>
    /// Starts a plugin
    /// </summary>
    /// <param name="pluginId">The plugin ID</param>
    /// <returns>True if start was successful</returns>
    public async Task<bool> StartPluginAsync(Guid pluginId)
    {
        const string location = $"{nameof(PluginsManager)}.{nameof(StartPluginAsync)}";

        try
        {
            var plugin = _plugins.FirstOrDefault(p => GeneratePluginId(p.PluginInfo!) == pluginId);

            if (plugin == null)
            {
                Log.Warning($"Plugin not found: {pluginId}");
                return await System.Threading.Tasks.Task.FromResult(false);
            }

            // TODO: Implement actual plugin startup logic
            // This would involve starting the loader process

            plugin.IsRunning = true;

            Log.Information($"Started plugin: {plugin.PluginInfo?.Name}");

            // Raise plugin status changed event
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
            {
                PluginId = pluginId,
                PluginName = plugin.PluginInfo?.Name ?? "Unknown",
                OldStatus = PluginStatus.Installed,
                NewStatus = PluginStatus.Running
            });

            return await System.Threading.Tasks.Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error starting plugin {pluginId}: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult(false);
        }
    }

    /// <summary>
    /// Stops a plugin
    /// </summary>
    /// <param name="pluginId">The plugin ID</param>
    /// <returns>True if stop was successful</returns>
    public async Task<bool> StopPluginAsync(Guid pluginId)
    {
        const string location = $"{nameof(PluginsManager)}.{nameof(StopPluginAsync)}";

        try
        {
            var plugin = _plugins.FirstOrDefault(p => GeneratePluginId(p.PluginInfo!) == pluginId);

            if (plugin == null)
            {
                Log.Warning($"Plugin not found: {pluginId}");
                return await System.Threading.Tasks.Task.FromResult(false);
            }

            // TODO: Implement actual plugin shutdown logic
            // This would involve stopping the loader process

            plugin.IsRunning = false;

            Log.Information($"Stopped plugin: {plugin.PluginInfo?.Name}");

            // Raise plugin status changed event
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
            {
                PluginId = pluginId,
                PluginName = plugin.PluginInfo?.Name ?? "Unknown",
                OldStatus = PluginStatus.Running,
                NewStatus = PluginStatus.Stopped
            });

            return await System.Threading.Tasks.Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error stopping plugin {pluginId}: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult(false);
        }
    }

    /// <summary>
    /// Calls a plugin function
    /// </summary>
    /// <param name="pluginId">The plugin ID</param>
    /// <param name="functionName">The function name</param>
    /// <param name="parameters">Optional parameters</param>
    /// <returns>The function result</returns>
    public async Task<object?> CallPluginFunctionAsync(Guid pluginId, string functionName, Dictionary<string, object>? parameters = null)
    {
        const string location = $"{nameof(PluginsManager)}.{nameof(CallPluginFunctionAsync)}";

        try
        {
            var plugin = _plugins.FirstOrDefault(p => GeneratePluginId(p.PluginInfo!) == pluginId);

            if (plugin == null)
            {
                Log.Warning($"Plugin not found: {pluginId}");
                return await System.Threading.Tasks.Task.FromResult<object?>(null);
            }

            if (!plugin.IsRunning)
            {
                Log.Warning($"Plugin is not running: {plugin.PluginInfo?.Name}");
                return await System.Threading.Tasks.Task.FromResult<object?>(null);
            }

            // TODO: Implement actual plugin function call
            // This would involve sending a request to the loader process

            Log.Information($"Called function {functionName} on plugin {plugin.PluginInfo?.Name}");

            return await System.Threading.Tasks.Task.FromResult<object?>(null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error calling function {functionName} on plugin {pluginId}: {ex.Message}");
            return await System.Threading.Tasks.Task.FromResult<object?>(null);
        }
    }
}

/// <summary>
/// Plugin installation implementation
/// </summary>
public class PluginInstallation : IPluginInstallation
{
    public string? InstallPath { get; set; }
    public PluginInfo? PluginInfo { get; set; }
    public LoaderInfo? LoaderInfo { get; set; }

    private List<DeviceLocator> _installedDevices = new();

    public IList<DeviceLocator> InstalledDevices
    {
        get => _installedDevices;
        set => _installedDevices = new List<DeviceLocator>(value);
    }

    public bool IsRunning { get; set; }
}
