using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Device;
using KitX.Core.DI;
using KitX.Core.Event;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using Serilog;

namespace KitX.Core.Plugin;

/// <summary>
/// Plugin manager for managing plugin installations and lifecycle
/// </summary>
public class PluginsManager : IPluginService
{
    private readonly List<PluginInstallation> _plugins = new();

    /// <summary>
    /// Tracks running loader processes keyed by plugin ID.
    /// Used to kill processes on stop and prevent orphan processes.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, Process> _pluginProcesses = new();

    /// <summary>
    /// TaskCompletionSource instances used to await plugin registration after starting a loader process.
    /// Key: plugin name (matching PluginInfo.Name).
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _registrationTcs = new();

    /// <summary>
    /// Default timeout for waiting a plugin to connect and register after process start.
    /// </summary>
    private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default timeout for waiting a plugin to gracefully disconnect after sending stop command.
    /// </summary>
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Default timeout for waiting a plugin function call response.
    /// </summary>
    private static readonly TimeSpan DefaultFunctionCallTimeout = TimeSpan.FromSeconds(30);

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

            // Stop the plugin if it's running before removing
            if (plugin.IsRunning)
            {
                await StopPluginAsync(pluginId);
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
    /// Starts a plugin by launching its loader process and waiting for it to register
    /// via the PluginsServer WebSocket connection.
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
                return false;
            }

            if (plugin.IsRunning)
            {
                Log.Warning("[PluginsManager] Plugin '{PluginName}' is already running, skipping start",
                    plugin.PluginInfo?.Name);
                return true;
            }

            var loaderInfo = plugin.LoaderInfo;
            if (loaderInfo == null)
            {
                Log.Error("[PluginsManager] Cannot start plugin '{PluginName}': LoaderInfo is null",
                    plugin.PluginInfo?.Name);
                return false;
            }

            // Self-loading plugins don't need a separate loader process — they connect on their own.
            // Just mark as running; the plugin is responsible for connecting to PluginsServer.
            if (loaderInfo.SelfLoad)
            {
                Log.Information("[PluginsManager] Plugin '{PluginName}' is self-loading, marking as started " +
                    "(plugin should connect to PluginsServer on its own)",
                    plugin.PluginInfo?.Name);
                plugin.IsRunning = true;
                PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
                {
                    PluginId = pluginId,
                    PluginName = plugin.PluginInfo?.Name ?? "Unknown",
                    OldStatus = PluginStatus.Installed,
                    NewStatus = PluginStatus.Running
                });
                return true;
            }

            // Resolve the PluginsServer to get the port for the --connect argument
            var pluginsServer = ResolvePluginsServer();
            if (pluginsServer == null)
            {
                Log.Error("[PluginsManager] Cannot start plugin '{PluginName}': PluginsServer not available",
                    plugin.PluginInfo?.Name);
                return false;
            }

            var serverPort = pluginsServer.Port;
            if (serverPort == null || serverPort <= 0)
            {
                Log.Error("[PluginsManager] Cannot start plugin '{PluginName}': PluginsServer port not assigned",
                    plugin.PluginInfo?.Name);
                return false;
            }

            // Resolve the loader executable path
            var loaderExePath = ResolveLoaderExecutablePath(loaderInfo);
            if (loaderExePath == null)
            {
                Log.Error("[PluginsManager] Cannot start plugin '{PluginName}': " +
                    "loader executable not found for LoaderName='{LoaderName}', LoaderFramework='{LoaderFramework}'",
                    plugin.PluginInfo?.Name, loaderInfo.LoaderName, loaderInfo.LoaderFramework);
                return false;
            }

            // Build the plugin root startup file path
            var pluginRootFile = !string.IsNullOrEmpty(plugin.PluginInfo?.RootStartupFileName)
                ? Path.Combine(plugin.InstallPath!, plugin.PluginInfo!.RootStartupFileName)
                : "";

            // Build command-line arguments: --load <plugin> --connect <IP>:<Port>
            var startArgs = BuildStartArguments(pluginRootFile, serverPort.Value);

            Log.Information("[PluginsManager] Starting plugin '{PluginName}' with loader: {LoaderExe} {Args}",
                plugin.PluginInfo?.Name, loaderExePath, startArgs);

            // Create a TaskCompletionSource to wait for the plugin to register via WebSocket
            var pluginName = plugin.PluginInfo!.Name;
            var registrationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _registrationTcs[pluginName] = registrationTcs;

            // Subscribe to the PluginRegistered event via EventService (the canonical event bus).
            // PluginsServer publishes PluginRegistered through EventService, not through its own C# event.
            var eventService = ResolveEventService();
            EventHandler<PluginRegisteredEventArgs>? registrationHandler = null;
            registrationHandler = (sender, args) =>
            {
                if (args.PluginInfo?.Name == pluginName)
                {
                    Log.Information("[PluginsManager] Plugin '{PluginName}' registered via WebSocket, " +
                        "completing start operation", pluginName);
                    registrationTcs.TrySetResult(true);
                }
            };

            if (eventService != null)
            {
                eventService.Subscribe(EventNames.PluginRegistered, registrationHandler);
            }
            else
            {
                Log.Warning("[PluginsManager] EventService not available, falling back to polling for registration");
            }

            try
            {
                // Determine the actual process filename and arguments.
                // For .NET DLLs, use 'dotnet <dllPath> <args>'.
                // For Python scripts, use 'python <scriptPath> <args>'.
                // For native executables, use the path directly.
                var (processFileName, processArgs) = BuildProcessStartInfo(
                    loaderExePath, startArgs, loaderInfo);

                // Launch the loader process
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = processFileName,
                        Arguments = processArgs,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = plugin.InstallPath!
                    }
                };

                process.EnableRaisingEvents = true;

                // Log process output for debugging
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log.Debug("[PluginLoader:{PluginName}] {Output}", pluginName, e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log.Warning("[PluginLoader:{PluginName}] {Error}", pluginName, e.Data);
                };

                if (!process.Start())
                {
                    Log.Error("[PluginsManager] Failed to start loader process for plugin '{PluginName}'",
                        pluginName);
                    CleanupRegistration(pluginName, registrationHandler, eventService);
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Track the process
                _pluginProcesses[pluginId] = process;

                Log.Information("[PluginsManager] Loader process started for plugin '{PluginName}', " +
                    "PID={ProcessId}, waiting for WebSocket registration (timeout={Timeout}s)",
                    pluginName, process.Id, DefaultStartTimeout.TotalSeconds);

                // Wait for the plugin to register via WebSocket, with timeout.
                // If EventService is available, we wait on the TCS; otherwise we poll.
                var registered = false;

                if (eventService != null)
                {
                    // Event-driven wait
                    using var cts = new CancellationTokenSource(DefaultStartTimeout);
                    using var ctsRegistration = cts.Token.Register(() => registrationTcs.TrySetCanceled());

                    try
                    {
                        registered = await registrationTcs.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        registered = false;
                    }
                }
                else
                {
                    // Polling fallback: check PluginsServer connections periodically
                    var startTime = DateTime.UtcNow;
                    while (DateTime.UtcNow - startTime < DefaultStartTimeout)
                    {
                        var connection = ((Contract.Plugin.IPluginServer)pluginsServer).Connections
                            .FirstOrDefault(c => c.PluginInfo?.Name == pluginName);
                        if (connection != null)
                        {
                            registered = true;
                            break;
                        }
                        await Task.Delay(500);
                    }
                }

                if (registered)
                {
                    Log.Information("[PluginsManager] Plugin '{PluginName}' started successfully", pluginName);
                    // Note: IsRunning is set by OnPluginStatusChanged when the registration event fires,
                    // but we set it here as well to ensure consistency.
                    plugin.IsRunning = true;
                    return true;
                }
                else
                {
                    // Timeout — roll back
                    Log.Warning("[PluginsManager] Plugin '{PluginName}' did not register within {Timeout}s, " +
                        "rolling back (killing loader process)", pluginName, DefaultStartTimeout.TotalSeconds);

                    KillPluginProcess(pluginId);
                    plugin.IsRunning = false;

                    PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
                    {
                        PluginId = pluginId,
                        PluginName = pluginName,
                        OldStatus = PluginStatus.Installed,
                        NewStatus = PluginStatus.Error
                    });

                    return false;
                }
            }
            finally
            {
                CleanupRegistration(pluginName, registrationHandler, eventService);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginsManager] {Location}: Error starting plugin {PluginId}", location, pluginId);
            return false;
        }
    }

    /// <summary>
    /// Stops a plugin by sending a stop command via WebSocket and waiting for
    /// graceful disconnection. Falls back to killing the loader process on timeout.
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
                return false;
            }

            if (!plugin.IsRunning)
            {
                Log.Warning("[PluginsManager] Plugin '{PluginName}' is not running, skipping stop",
                    plugin.PluginInfo?.Name);
                return true;
            }

            var pluginName = plugin.PluginInfo?.Name ?? "Unknown";
            var pluginsServer = ResolvePluginsServer();

            // Try graceful shutdown via WebSocket command
            var gracefulStopSucceeded = false;

            if (pluginsServer != null)
            {
                var connection = FindConnectionByPluginName(pluginsServer, pluginName);
                if (connection != null)
                {
                    Log.Information("[PluginsManager] Sending stop command to plugin '{PluginName}' " +
                        "via WebSocket (ConnectionId={ConnectionId})",
                        pluginName, connection.ConnectionId);

                    // Build and send a stop command
                    try
                    {
                        var stopCommand = new Command
                        {
                            Request = CommandRequestInfo.ReceiveCommand,
                            Tags = new Dictionary<string, string>
                            {
                                ["Action"] = "Stop"
                            }
                        };

                        var request = new Request
                        {
                            Type = RequestTypes.Command,
                            Version = RequestVersions.V1,
                            Content = JsonSerializer.Serialize(stopCommand)
                        };

                        connection.Send(JsonSerializer.Serialize(request));
                        gracefulStopSucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[PluginsManager] Failed to send stop command to plugin '{PluginName}'",
                            pluginName);
                    }
                }
                else
                {
                    Log.Warning("[PluginsManager] No WebSocket connection found for plugin '{PluginName}', " +
                        "will kill loader process directly", pluginName);
                }
            }

            // If we sent a stop command, wait for the plugin to disconnect gracefully
            if (gracefulStopSucceeded && pluginsServer != null)
            {
                var disconnected = await WaitForPluginDisconnection(pluginsServer, pluginName, DefaultStopTimeout);

                if (disconnected)
                {
                    Log.Information("[PluginsManager] Plugin '{PluginName}' disconnected gracefully", pluginName);
                }
                else
                {
                    Log.Warning("[PluginsManager] Plugin '{PluginName}' did not disconnect within {Timeout}s, " +
                        "forcing process termination", pluginName, DefaultStopTimeout.TotalSeconds);
                }
            }

            // Kill the loader process if still running
            KillPluginProcess(pluginId);

            // Ensure IsRunning is reset
            plugin.IsRunning = false;

            Log.Information("[PluginsManager] Stopped plugin: {PluginName}", pluginName);

            // Raise plugin status changed event
            PluginStatusChanged?.Invoke(this, new PluginStatusChangedEventArgs
            {
                PluginId = pluginId,
                PluginName = pluginName,
                OldStatus = PluginStatus.Running,
                NewStatus = PluginStatus.Stopped
            });

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginsManager] {Location}: Error stopping plugin {PluginId}", location, pluginId);
            return false;
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
    /// Calls a plugin function by sending a command via the PluginsServer WebSocket
    /// connection and awaiting the plugin's response. Reuses the same request/response
    /// correlation mechanism as <see cref="DevicesServer.HandlePluginInvokeAsync"/>.
    /// </summary>
    /// <param name="pluginId">The plugin ID</param>
    /// <param name="functionName">The function name</param>
    /// <param name="parameters">Optional parameters (key-value pairs)</param>
    /// <returns>The function result, or null if the call failed or timed out</returns>
    public async Task<object?> CallPluginFunctionAsync(Guid pluginId, string functionName, Dictionary<string, object>? parameters = null)
    {
        const string location = $"{nameof(PluginsManager)}.{nameof(CallPluginFunctionAsync)}";

        try
        {
            var plugin = _plugins.FirstOrDefault(p => GeneratePluginId(p.PluginInfo!) == pluginId);

            if (plugin == null)
            {
                Log.Warning("[PluginsManager] Plugin not found: {PluginId}", pluginId);
                return null;
            }

            if (!plugin.IsRunning)
            {
                Log.Warning("[PluginsManager] Plugin is not running: {PluginName}", plugin.PluginInfo?.Name);
                return null;
            }

            var pluginName = plugin.PluginInfo?.Name;
            if (string.IsNullOrEmpty(pluginName))
            {
                Log.Warning("[PluginsManager] Plugin name is null for plugin {PluginId}", pluginId);
                return null;
            }

            // Resolve PluginsServer and find the connection for this plugin
            var pluginsServer = ResolvePluginsServer();
            if (pluginsServer == null)
            {
                Log.Error("[PluginsManager] Cannot call function: PluginsServer not available");
                return null;
            }

            var connection = FindConnectionByPluginName(pluginsServer, pluginName);
            if (connection == null)
            {
                Log.Warning("[PluginsManager] No WebSocket connection found for plugin '{PluginName}'", pluginName);
                return null;
            }

            // Convert parameters dictionary to Parameter list
            var functionArgs = new List<Parameter>();
            if (parameters != null)
            {
                foreach (var kvp in parameters)
                {
                    functionArgs.Add(new Parameter
                    {
                        Name = kvp.Key,
                        Type = kvp.Value?.GetType().Name ?? "Object",
                        Value = kvp.Value?.ToString() ?? string.Empty,
                        IsOptional = false
                    });
                }
            }

            // Generate RequestId for correlating the async response
            var requestId = Guid.NewGuid().ToString();

            // Build the Command with function call details
            var command = new Command
            {
                SendTime = DateTime.UtcNow,
                Request = CommandRequestInfo.ReceiveCommand,
                PluginConnectionId = connection.ConnectionId ?? string.Empty,
                FunctionName = functionName,
                FunctionArgs = functionArgs,
                Tags = new Dictionary<string, string>
                {
                    ["RequestId"] = requestId
                }
            };

            // Build the Request wrapping the Command
            var request = new Request
            {
                Type = RequestTypes.Command,
                Version = RequestVersions.V1,
                Content = JsonSerializer.Serialize(command)
            };

            // Set up TaskCompletionSource to await the plugin response
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Subscribe to PluginResponse via EventService (the canonical event bus)
            var eventService = ResolveEventService();
            if (eventService == null)
            {
                Log.Error("[PluginsManager] Cannot call function: EventService not available");
                return null;
            }

            EventHandler<PluginResponseEventArgs>? responseHandler = null;
            responseHandler = (sender, args) =>
            {
                if (args.RequestId == requestId)
                {
                    tcs.TrySetResult(args.Content);
                }
            };
            eventService.Subscribe<PluginResponseEventArgs>(EventNames.PluginResponse, responseHandler);

            try
            {
                // Send the request to the plugin via WebSocket
                var requestJson = JsonSerializer.Serialize(request);
                connection.Send(requestJson);

                Log.Information("[PluginsManager] Sent function call '{FunctionName}' to plugin '{PluginName}', " +
                    "RequestId: {RequestId}", functionName, pluginName, requestId);

                // Wait for response with timeout
                using var cts = new CancellationTokenSource(DefaultFunctionCallTimeout);
                using var ctsRegistration = cts.Token.Register(() => tcs.TrySetCanceled());

                var responseContent = await tcs.Task;

                // Deserialize the response Command to extract the return value
                try
                {
                    var responseCommand = JsonSerializer.Deserialize<Command>(responseContent);

                    // The response body contains the function result
                    if (responseCommand.Body != null && responseCommand.BodyLength > 0)
                    {
                        var resultJson = Encoding.UTF8.GetString(
                            responseCommand.Body.AsSpan(0, responseCommand.BodyLength).ToArray());
                        var result = JsonSerializer.Deserialize<object>(resultJson);
                        Log.Information("[PluginsManager] Function '{FunctionName}' on plugin '{PluginName}' " +
                            "returned result", functionName, pluginName);
                        return result;
                    }

                    // Fallback: try to extract result from Tags
                    if (responseCommand.Tags != null &&
                        responseCommand.Tags.TryGetValue("Result", out var resultValue))
                    {
                        Log.Information("[PluginsManager] Function '{FunctionName}' on plugin '{PluginName}' " +
                            "returned result from Tags", functionName, pluginName);
                        return resultValue;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[PluginsManager] Failed to deserialize function response, " +
                        "returning raw content");
                    return responseContent;
                }

                Log.Information("[PluginsManager] Function '{FunctionName}' on plugin '{PluginName}' " +
                    "completed with no return value", functionName, pluginName);
                return null;
            }
            catch (OperationCanceledException)
            {
                Log.Warning("[PluginsManager] Function call '{FunctionName}' on plugin '{PluginName}' " +
                    "timed out after {Timeout}s", functionName, pluginName,
                    DefaultFunctionCallTimeout.TotalSeconds);
                return null;
            }
            finally
            {
                eventService.Unsubscribe<PluginResponseEventArgs>(
                    EventNames.PluginResponse, responseHandler);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginsManager] {Location}: Error calling function {FunctionName} " +
                "on plugin {PluginId}", location, functionName, pluginId);
            return null;
        }
    }

    // ──────────────────────────── Private Helpers ────────────────────────────

    /// <summary>
    /// Resolves the PluginsServer instance from the DI container.
    /// Returns null if ServiceHost is not initialized or the server is not available.
    /// </summary>
    private PluginsServer? ResolvePluginsServer()
    {
        try
        {
            if (!ServiceHost.IsInitialized)
            {
                Log.Warning("[PluginsManager] ServiceHost not initialized, cannot resolve PluginsServer");
                return null;
            }

            var server = ServiceHost.GetRequiredService<Contract.Plugin.IPluginServer>() as PluginsServer;
            if (server == null)
                Log.Warning("[PluginsManager] IPluginServer is not a PluginsServer instance");

            return server;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginsManager] Failed to resolve PluginsServer from ServiceHost");
            return null;
        }
    }

    /// <summary>
    /// Resolves the EventService instance from the DI container.
    /// Returns null if ServiceHost is not initialized or the service is not available.
    /// </summary>
    private EventService? ResolveEventService()
    {
        try
        {
            if (!ServiceHost.IsInitialized)
                return null;

            return ServiceHost.GetRequiredService<IEventService>() as EventService;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[PluginsManager] Failed to resolve EventService from ServiceHost");
            return null;
        }
    }

    /// <summary>
    /// Resolves the loader executable path based on LoaderInfo metadata.
    /// Searches in the configured loaders install path (e.g., ./Loaders/) for a matching
    /// loader directory named after LoaderName, then looks for the executable.
    /// For .NET loaders, returns the DLL path (the process will be launched via 'dotnet').
    /// </summary>
    private string? ResolveLoaderExecutablePath(LoaderInfo loaderInfo)
    {
        try
        {
            // Read the loaders install path from configuration
            var loadersInstallPath = GetLoadersInstallPath();
            if (string.IsNullOrEmpty(loadersInstallPath))
            {
                Log.Warning("[PluginsManager] Loaders install path is not configured");
                return null;
            }

            var fullPath = Path.GetFullPath(loadersInstallPath);
            if (!Directory.Exists(fullPath))
            {
                Log.Warning("[PluginsManager] Loaders directory does not exist: {Path}", fullPath);
                return null;
            }

            // Look for a subdirectory matching the LoaderName
            var loaderDir = Path.Combine(fullPath, loaderInfo.LoaderName);
            if (!Directory.Exists(loaderDir))
            {
                // Fallback: search all subdirectories for a loader matching LoaderName
                var matchingDir = Directory.GetDirectories(fullPath)
                    .FirstOrDefault(d =>
                    {
                        var dirName = Path.GetFileName(d);
                        return dirName.Equals(loaderInfo.LoaderName, StringComparison.OrdinalIgnoreCase)
                            || dirName.StartsWith(loaderInfo.LoaderName, StringComparison.OrdinalIgnoreCase);
                    });

                if (matchingDir != null)
                    loaderDir = matchingDir;
                else
                {
                    Log.Warning("[PluginsManager] Loader directory not found for LoaderName='{LoaderName}' in {Path}",
                        loaderInfo.LoaderName, fullPath);
                    return null;
                }
            }

            // Determine the executable name based on LoaderFramework
            var exeName = DetermineLoaderExecutableName(loaderInfo);
            var exePath = Path.Combine(loaderDir, exeName);

            if (File.Exists(exePath))
            {
                Log.Information("[PluginsManager] Found loader executable: {ExePath}", exePath);
                return exePath;
            }

            // For .NET loaders, the executable might be under a publish directory
            var publishDir = Path.Combine(loaderDir, "publish");
            if (Directory.Exists(publishDir))
            {
                exePath = Path.Combine(publishDir, exeName);
                if (File.Exists(exePath))
                {
                    Log.Information("[PluginsManager] Found loader executable in publish dir: {ExePath}", exePath);
                    return exePath;
                }
            }

            // Try with .dll extension for dotnet execution
            if (loaderInfo.LoaderFramework.Equals(".NET", StringComparison.OrdinalIgnoreCase)
                || loaderInfo.LoaderLanguage.Equals("C#", StringComparison.OrdinalIgnoreCase))
            {
                var dllName = Path.GetFileNameWithoutExtension(exeName) + ".dll";
                var dllPath = Path.Combine(loaderDir, dllName);
                if (File.Exists(dllPath))
                {
                    Log.Information("[PluginsManager] Found loader DLL for dotnet execution: {DllPath}", dllPath);
                    return dllPath;
                }

                if (Directory.Exists(publishDir))
                {
                    dllPath = Path.Combine(publishDir, dllName);
                    if (File.Exists(dllPath))
                    {
                        Log.Information("[PluginsManager] Found loader DLL in publish dir: {DllPath}", dllPath);
                        return dllPath;
                    }
                }
            }

            Log.Warning("[PluginsManager] Loader executable not found: {ExeName} in {LoaderDir}", exeName, loaderDir);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginsManager] Error resolving loader executable path");
            return null;
        }
    }

    /// <summary>
    /// Determines the loader executable file name based on the LoaderFramework and LoaderLanguage.
    /// </summary>
    private static string DetermineLoaderExecutableName(LoaderInfo loaderInfo)
    {
        // For .NET/C# loaders, use dotnet to run the DLL
        if (loaderInfo.LoaderFramework.Equals(".NET", StringComparison.OrdinalIgnoreCase)
            || loaderInfo.LoaderLanguage.Equals("C#", StringComparison.OrdinalIgnoreCase))
        {
            // The actual file might be a DLL, but we return the expected name;
            // ResolveLoaderExecutablePath will handle the dotnet vs direct execution.
            return $"{loaderInfo.LoaderName}.dll";
        }

        // For Python loaders
        if (loaderInfo.LoaderLanguage.Equals("Python", StringComparison.OrdinalIgnoreCase))
        {
            return "main.py";
        }

        // Default: assume the loader name is the executable name
        return $"{loaderInfo.LoaderName}.exe";
    }

    /// <summary>
    /// Gets the loaders install path from configuration, falling back to the default "./Loaders/".
    /// </summary>
    private static string GetLoadersInstallPath()
    {
        try
        {
            if (ServiceHost.IsInitialized)
            {
                var configService = ServiceHost.GetRequiredService<IConfigService>();
                var loadersConf = configService.AppConfig?.Loaders;
                if (loadersConf != null && !string.IsNullOrEmpty(loadersConf.InstallPath))
                    return loadersConf.InstallPath;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[PluginsManager] Could not read loaders install path from config, using default");
        }

        return "./Loaders/";
    }

    /// <summary>
    /// Builds the command-line arguments for starting a loader process.
    /// Format: --load "<pluginRootFile>" --connect 127.0.0.1:<port>
    /// </summary>
    private static string BuildStartArguments(string pluginRootFile, int serverPort)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(pluginRootFile))
            sb.Append($"--load \"{pluginRootFile}\" ");
        sb.Append($"--connect 127.0.0.1:{serverPort}");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the process start info (FileName, Arguments) based on the loader type.
    /// For .NET DLLs: uses 'dotnet' as FileName with the DLL path as the first argument.
    /// For Python scripts: uses 'python' as FileName with the script path as the first argument.
    /// For native executables: uses the path directly as FileName.
    /// </summary>
    private static (string fileName, string arguments) BuildProcessStartInfo(
        string loaderExePath, string startArgs, LoaderInfo loaderInfo)
    {
        var isDotNet = loaderInfo.LoaderFramework.Equals(".NET", StringComparison.OrdinalIgnoreCase)
            || loaderInfo.LoaderLanguage.Equals("C#", StringComparison.OrdinalIgnoreCase);
        var isPython = loaderInfo.LoaderLanguage.Equals("Python", StringComparison.OrdinalIgnoreCase);

        if (isDotNet && loaderExePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            // dotnet <dllPath> <args>
            return ("dotnet", $"\"{loaderExePath}\" {startArgs}");
        }

        if (isPython && loaderExePath.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        {
            // python <scriptPath> <args>
            return ("python", $"\"{loaderExePath}\" {startArgs}");
        }

        // Native executable: run directly
        return (loaderExePath, startArgs);
    }

    /// <summary>
    /// Finds a WebSocket connection by plugin name from the PluginsServer.
    /// </summary>
    private static IPluginConnection? FindConnectionByPluginName(PluginsServer pluginsServer, string pluginName)
    {
        try
        {
            return ((Contract.Plugin.IPluginServer)pluginsServer).Connections
                .FirstOrDefault(c => c.PluginInfo?.Name == pluginName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginsManager] Error finding connection for plugin '{PluginName}'", pluginName);
            return null;
        }
    }

    /// <summary>
    /// Waits for a plugin to disconnect from the PluginsServer within the specified timeout.
    /// Polls the connection list to detect disconnection.
    /// </summary>
    private static async Task<bool> WaitForPluginDisconnection(PluginsServer pluginsServer, string pluginName, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < timeout)
        {
            var connection = ((Contract.Plugin.IPluginServer)pluginsServer).Connections
                .FirstOrDefault(c => c.PluginInfo?.Name == pluginName);

            if (connection == null)
                return true; // Plugin has disconnected

            await Task.Delay(200);
        }

        return false; // Timeout — plugin still connected
    }

    /// <summary>
    /// Kills the loader process for a plugin (if tracked) and removes it from the process dictionary.
    /// </summary>
    private void KillPluginProcess(Guid pluginId)
    {
        if (_pluginProcesses.TryRemove(pluginId, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    Log.Information("[PluginsManager] Killing loader process PID={ProcessId} for plugin {PluginId}",
                        process.Id, pluginId);
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException ex)
            {
                Log.Debug(ex, "[PluginsManager] Process already exited for plugin {PluginId}", pluginId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PluginsManager] Error killing loader process for plugin {PluginId}", pluginId);
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }
    }

    /// <summary>
    /// Cleans up the registration TaskCompletionSource and unsubscribes the event handler from EventService.
    /// </summary>
    private void CleanupRegistration(string pluginName, EventHandler<PluginRegisteredEventArgs> handler, EventService? eventService)
    {
        _registrationTcs.TryRemove(pluginName, out _);

        if (eventService != null)
        {
            try
            {
                eventService.Unsubscribe(EventNames.PluginRegistered, handler);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[PluginsManager] Error unsubscribing from PluginRegistered event");
            }
        }
    }
}
