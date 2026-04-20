using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;
using Serilog;
using KitX.Core.DI;

namespace KitX.Core.Plugin;

/// <summary>
/// Plugin manager for managing plugin installations and lifecycle
/// </summary>
public class PluginsManager : IPluginService
{
    /// <summary>
    /// Gets the singleton instance (resolves from ServiceHost when available).
    /// Internal code should use constructor injection instead.
    /// </summary>
    public static PluginsManager Instance
    {
        get
        {
            if (ServiceHost.IsInitialized)
                return (PluginsManager)ServiceHost.GetRequiredService<IPluginService>();
            Log.Error("[PluginsManager] Instance: ServiceHost not initialized! Returning orphan instance — " +
                "this indicates a DI initialization order bug. Use ServiceHost/constructor injection instead.");
            return new PluginsManager();
        }
    }

    /// <summary>
    /// Kept for backward compatibility — ServiceHost is now the single source of truth.
    /// </summary>
    [Obsolete("ServiceHost is now the single source of truth. This method is a no-op.")]
    internal static void SetServiceProvider(IServiceProvider? sp) { /* no-op */ }

    private readonly List<PluginInstallation> _plugins = new();

    /// <summary>
    /// Event raised when plugin status changes
    /// </summary>
    public event EventHandler<PluginStatusChangedEventArgs>? PluginStatusChanged;

    /// <summary>
    /// Creates a new plugins manager
    /// </summary>
    public PluginsManager()
    {
        // Load installed plugins on startup
        LoadInstalledPlugins();
    }

    /// <summary>
    /// Loads installed plugins from the plugins directory
    /// </summary>
    private void LoadInstalledPlugins()
    {
        try
        {
            var pluginsDir = Path.GetFullPath("./Data/Plugins/");
            if (!Directory.Exists(pluginsDir))
            {
                Log.Information("Plugins directory does not exist, creating: {Dir}", pluginsDir);
                Directory.CreateDirectory(pluginsDir);
                return;
            }

            var pluginDirs = Directory.GetDirectories(pluginsDir);
            Log.Information("Found {Count} plugin directories to load", pluginDirs.Length);

            foreach (var pluginDir in pluginDirs)
            {
                try
                {
                    var pluginInfoPath = Path.Combine(pluginDir, "PluginInfo.json");
                    var loaderInfoPath = Path.Combine(pluginDir, "LoaderInfo.json");

                    if (!File.Exists(pluginInfoPath))
                    {
                        Log.Warning("PluginInfo.json not found in {Dir}", pluginDir);
                        continue;
                    }

                    // Read PluginInfo.json
                    var pluginInfoJson = File.ReadAllText(pluginInfoPath);
                    var pluginInfo = JsonSerializer.Deserialize<PluginInfo>(pluginInfoJson);

                    if (pluginInfo == null)
                    {
                        Log.Warning("Failed to deserialize PluginInfo in {Dir}", pluginDir);
                        continue;
                    }

                    // Read LoaderInfo.json if exists
                    LoaderInfo? loaderInfo = null;
                    if (File.Exists(loaderInfoPath))
                    {
                        var loaderInfoJson = File.ReadAllText(loaderInfoPath);
                        loaderInfo = JsonSerializer.Deserialize<LoaderInfo>(loaderInfoJson);
                    }

                    // Create installation record
                    var installation = new PluginInstallation
                    {
                        Id = GeneratePluginId(pluginInfo),
                        InstallPath = pluginDir,
                        PluginInfo = pluginInfo,
                        LoaderInfo = loaderInfo ?? new LoaderInfo(),
                        InstalledDevices = new List<DeviceLocator>()
                    };

                    _plugins.Add(installation);
                    Log.Information("Loaded plugin: {Name} v{Version} from {Dir}", pluginInfo.Name, pluginInfo.Version, pluginDir);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error loading plugin from {Dir}", pluginDir);
                }
            }

            Log.Information("Loaded {Count} installed plugins", _plugins.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading installed plugins");
        }
    }

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
        return _plugins.FirstOrDefault(p => p.Id == pluginId);
    }

    /// <summary>
    /// Generates a plugin ID from plugin info
    /// </summary>
    /// <param name="pluginInfo">The plugin info</param>
    /// <returns>The generated plugin ID</returns>
    public static Guid GeneratePluginId(PluginInfo pluginInfo)
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

            // Get the plugins directory
            var pluginsDir = Path.GetFullPath("./Data/Plugins/");
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
            }

            // Generate a unique plugin ID based on filename
            var pluginFileName = Path.GetFileNameWithoutExtension(kxpFilePath);
            var pluginDir = Path.Combine(pluginsDir, pluginFileName);

            // Handle duplicate plugin names
            var counter = 1;
            while (Directory.Exists(pluginDir))
            {
                pluginDir = Path.Combine(pluginsDir, $"{pluginFileName}_{counter}");
                counter++;
            }

            Directory.CreateDirectory(pluginDir);

            // Decode the KXP file
            try
            {
                var decoder = new FileFormats.CSharp.ExtensionsPackage.Decoder(kxpFilePath);
                decoder.Decode(pluginDir);
                Log.Information($"Decoded KXP file to: {pluginDir}");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to decode KXP file, trying as direct files...");

                // If KXP decode fails, assume it's a directory with files already extracted
                // Just copy the source directory contents
                var sourceDir = Path.GetDirectoryName(kxpFilePath);
                if (sourceDir != null && Directory.Exists(sourceDir))
                {
                    foreach (var file in Directory.GetFiles(sourceDir))
                    {
                        var destFile = Path.Combine(pluginDir, Path.GetFileName(file));
                        if (!File.Exists(destFile))
                        {
                            File.Copy(file, destFile);
                        }
                    }
                }
            }

            // Look for LoaderStruct.json and PluginStruct.json in the extracted files
            var loaderStructPath = Path.Combine(pluginDir, "LoaderStruct.json");
            var pluginStructPath = Path.Combine(pluginDir, "PluginStruct.json");

            PluginInfo? pluginInfo = null;
            LoaderInfo? loaderInfo = null;

            // Parse LoaderStruct.json if exists
            if (File.Exists(loaderStructPath))
            {
                try
                {
                    var loaderStructJson = await File.ReadAllTextAsync(loaderStructPath);
                    var loaderStruct = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(loaderStructJson);

                    loaderInfo = new LoaderInfo
                    {
                        LoaderName = loaderStruct.TryGetProperty("LoaderName", out var name) ? name.GetString() ?? "Unknown" : "Unknown",
                        LoaderVersion = loaderStruct.TryGetProperty("LoaderVersion", out var version) ? version.GetString() ?? "1.0.0" : "1.0.0",
                        LoaderLanguage = loaderStruct.TryGetProperty("LoaderLanguage", out var lang) ? lang.GetString() ?? "Unknown" : "Unknown",
                        LoaderFramework = loaderStruct.TryGetProperty("LoaderFramework", out var fw) ? fw.GetString() ?? "Unknown" : "Unknown",
                        SelfLoad = loaderStruct.TryGetProperty("SelfLoad", out var selfLoad) && selfLoad.GetBoolean(),
                        Tags = new Dictionary<string, string>()
                    };

                    // Copy loader struct to LoaderInfo.json (different format)
                    var loaderInfoJson = System.Text.Json.JsonSerializer.Serialize(loaderInfo, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(Path.Combine(pluginDir, "LoaderInfo.json"), loaderInfoJson);

                    Log.Information($"Parsed LoaderStruct: {loaderInfo.LoaderName} v{loaderInfo.LoaderVersion}");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse LoaderStruct.json");
                }
            }

            // Parse PluginStruct.json if exists (or look for embedded plugin info)
            if (File.Exists(pluginStructPath))
            {
                try
                {
                    var pluginStructJson = await File.ReadAllTextAsync(pluginStructPath);
                    var pluginStruct = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(pluginStructJson);

                    pluginInfo = new PluginInfo
                    {
                        Name = pluginStruct.TryGetProperty("Name", out var name) ? name.GetString() ?? "Unknown" : "Unknown",
                        Version = pluginStruct.TryGetProperty("Version", out var ver) ? ver.GetString() ?? "1.0.0" : "1.0.0",
                        DisplayName = new Dictionary<string, string>(),
                        SimpleDescription = new Dictionary<string, string>(),
                        ComplexDescription = new Dictionary<string, string>(),
                        TotalDescriptionInMarkdown = new Dictionary<string, string>(),
                        Tags = new Dictionary<string, string>(),
                        Functions = new List<Function>()
                    };

                    // Parse DisplayName
                    if (pluginStruct.TryGetProperty("DisplayName", out var displayName))
                    {
                        foreach (var prop in displayName.EnumerateObject())
                        {
                            pluginInfo.DisplayName[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }

                    // Parse SimpleDescription
                    if (pluginStruct.TryGetProperty("SimpleDescription", out var simpleDesc))
                    {
                        foreach (var prop in simpleDesc.EnumerateObject())
                        {
                            pluginInfo.SimpleDescription[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }

                    // Parse ComplexDescription
                    if (pluginStruct.TryGetProperty("ComplexDescription", out var complexDesc))
                    {
                        foreach (var prop in complexDesc.EnumerateObject())
                        {
                            pluginInfo.ComplexDescription[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }

                    // Parse other fields
                    pluginInfo.AuthorName = pluginStruct.TryGetProperty("AuthorName", out var author) ? author.GetString() ?? "Unknown" : "Unknown";
                    pluginInfo.AuthorLink = pluginStruct.TryGetProperty("AuthorLink", out var authorLink) ? authorLink.GetString() ?? "" : "";
                    pluginInfo.PublisherName = pluginStruct.TryGetProperty("PublisherName", out var publisher) ? publisher.GetString() ?? "Unknown" : "Unknown";
                    pluginInfo.PublisherLink = pluginStruct.TryGetProperty("PublisherLink", out var publisherLink) ? publisherLink.GetString() ?? "" : "";
                    pluginInfo.IconInBase64 = pluginStruct.TryGetProperty("IconInBase64", out var icon) ? icon.GetString() ?? "" : "";
                    pluginInfo.IsMarketVersion = pluginStruct.TryGetProperty("IsMarketVersion", out var marketVer) && marketVer.GetBoolean();
                    pluginInfo.RootStartupFileName = pluginStruct.TryGetProperty("RootStartupFileName", out var rootFile) ? rootFile.GetString() ?? "" : "";

                    if (pluginStruct.TryGetProperty("PublishDate", out var publishDate))
                    {
                        if (DateTime.TryParse(publishDate.GetString(), out var pd))
                            pluginInfo.PublishDate = pd;
                    }

                    if (pluginStruct.TryGetProperty("LastUpdateDate", out var lastUpdate))
                    {
                        if (DateTime.TryParse(lastUpdate.GetString(), out var lud))
                            pluginInfo.LastUpdateDate = lud;
                    }

                    Log.Information($"Parsed PluginStruct: {pluginInfo.Name} v{pluginInfo.Version}");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse PluginStruct.json");
                }
            }

            // If no plugin info from KXP, create basic info from filename
            if (pluginInfo == null)
            {
                pluginInfo = new PluginInfo
                {
                    Name = pluginFileName,
                    Version = "1.0.0",
                    PublisherName = "Unknown",
                    AuthorName = "Unknown",
                    DisplayName = new Dictionary<string, string> { { "en-us", pluginFileName } },
                    SimpleDescription = new Dictionary<string, string> { { "en-us", "Imported plugin" } },
                    ComplexDescription = new Dictionary<string, string> { { "en-us", "Imported plugin" } },
                    TotalDescriptionInMarkdown = new Dictionary<string, string>(),
                    Tags = new Dictionary<string, string>(),
                    Functions = new List<Function>()
                };
            }

            // Validate RootStartupFileName is specified and exists
            if (string.IsNullOrEmpty(pluginInfo.RootStartupFileName))
            {
                Log.Error($"Plugin import failed: RootStartupFileName is not specified in plugin {pluginInfo.Name}. Please ensure the plugin package includes this field.");
                return false;
            }

            var pluginFilePath = Path.Combine(pluginDir, pluginInfo.RootStartupFileName);
            if (!File.Exists(pluginFilePath))
            {
                Log.Error($"Plugin import failed: RootStartupFileName '{pluginInfo.RootStartupFileName}' points to a non-existent file in plugin {pluginInfo.Name}. File not found at: {pluginFilePath}");
                // Clean up the created directory
                try { Directory.Delete(pluginDir, true); } catch { }
                return false;
            }

            // Create PluginInfo.json
            var pluginInfoJson = System.Text.Json.JsonSerializer.Serialize(pluginInfo, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(pluginDir, "PluginInfo.json"), pluginInfoJson);

            // Create installation record
            var installation = new PluginInstallation
            {
                Id = GeneratePluginId(pluginInfo),
                InstallPath = pluginDir,
                PluginInfo = pluginInfo,
                LoaderInfo = loaderInfo ?? new LoaderInfo(),
                InstalledDevices = new List<DeviceLocator>()
            };

            _plugins.Add(installation);

            Log.Information($"Imported plugin: {pluginInfo.Name} (v{pluginInfo.Version}) to {pluginDir}");

            // Raise plugin status changed event
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
            {
                PluginId = installation.Id,
                PluginName = pluginInfo.Name,
                OldStatus = PluginStatus.Unknown,
                NewStatus = PluginStatus.Installed
            });

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"In {location}: Error importing plugin {kxpFilePath}: {ex.Message}");
            return false;
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
    /// Called by PluginsServer when a plugin's connection status changes (Running/Pending/Errored).
    /// Updates internal state and fires PluginStatusChanged event so the Dashboard UI refreshes.
    /// Unlike UpdatePluginRunningState, this method handles all ServerStatus-to-PluginStatus mappings
    /// including the Error state.
    /// </summary>
    /// <param name="pluginName">The plugin name</param>
    /// <param name="newStatus">The new status from the connection layer</param>
    public void OnPluginStatusChanged(string pluginName, PluginStatus newStatus)
    {
        try
        {
            var plugin = _plugins.FirstOrDefault(p => p.PluginInfo?.Name == pluginName);
            if (plugin == null)
            {
                Log.Debug("[PluginsManager] OnPluginStatusChanged: plugin '{PluginName}' not found in installed list, ignoring", pluginName);
                return;
            }

            var oldStatus = plugin.IsRunning ? PluginStatus.Running : PluginStatus.Installed;

            // Update IsRunning based on connection status
            plugin.IsRunning = newStatus == PluginStatus.Running;

            Log.Information("[PluginsManager] Plugin '{PluginName}' status changed: {OldStatus} -> {NewStatus}",
                pluginName, oldStatus, newStatus);

            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
            {
                PluginId = plugin.Id,
                PluginName = pluginName,
                OldStatus = oldStatus,
                NewStatus = newStatus
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginsManager] Error in OnPluginStatusChanged for plugin '{PluginName}'", pluginName);
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
