using System;
using System.Collections.Generic;
using System.IO;
using KitX.Core.Contract.Configuration;
using Serilog;

namespace KitX.Core.Configuration;

/// <summary>
/// Configuration manager for managing application configurations
/// Coordinates ConfigLoader, ConfigSaver, and file watching
/// </summary>
public class ConfigManager : IConfigService, IDisposable
{
    private static ConfigManager? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    public static ConfigManager Instance => _instance ??= new();

    private string? _configLocation;

    private readonly Dictionary<string, object> _configs = new();

    /// <summary>
    /// File system watchers for hot-reload
    /// </summary>
    private readonly Dictionary<string, FileSystemWatcher> _fileWatchers = new();

    /// <summary>
    /// Exception counts to prevent infinite loops when saving files
    /// </summary>
    private readonly Dictionary<string, int> _exceptCounts = new();

    private readonly IConfigLoader _loader;
    private readonly IConfigSaver _saver;

    /// <summary>
    /// Whether hot-reload is enabled
    /// </summary>
    public bool HotReloadEnabled { get; set; } = true;

    /// <summary>
    /// Event raised when configuration changes
    /// </summary>
    public event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    /// <summary>
    /// Gets the application configuration
    /// </summary>
    public IAppConfig AppConfig { get; private set; } = new AppConfig();

    /// <summary>
    /// Gets the typed application configuration (strong type version)
    /// </summary>
    public AppConfig TypedAppConfig => (AppConfig)AppConfig;

    public IAnnouncementConfig AnnouncementConfig { get; set; } = new AnnouncementConfig();

    /// <summary>
    /// Gets the typed announcement configuration (strong type version)
    /// </summary>
    public AnnouncementConfig TypedAnnouncementConfig => (AnnouncementConfig)AnnouncementConfig;

    /// <summary>
    /// Gets the plugins configuration
    /// </summary>
    public IPluginsConfig PluginsConfig { get; private set; } = new PluginsConfig();

    /// <summary>
    /// Gets the security configuration
    /// </summary>
    public ISecurityConfig SecurityConfig { get; private set; } = new SecurityConfig();

    /// <summary>
    /// Gets the typed security configuration (strong type version)
    /// </summary>
    public SecurityConfig TypedSecurityConfig => (SecurityConfig)SecurityConfig;

    /// <summary>
    /// Private constructor
    /// </summary>
    private ConfigManager()
    {
        _loader = new ConfigLoader();
        _saver = new ConfigSaver();
    }

    /// <summary>
    /// Sets the configuration file location
    /// </summary>
    /// <param name="location">The directory path</param>
    /// <returns>The config manager instance</returns>
    public ConfigManager SetLocation(string location)
    {
        _configLocation = Path.GetFullPath(location);

        if (!Directory.Exists(_configLocation))
        {
            Directory.CreateDirectory(_configLocation);
        }

        return this;
    }

    /// <summary>
    /// Loads all configurations from files
    /// </summary>
    public void Load()
    {
        if (string.IsNullOrEmpty(_configLocation))
        {
            SetLocation("./Config/");
        }

        AppConfig = _loader.Load<AppConfig>(_configLocation, "AppConfig.json");
        PluginsConfig = _loader.Load<PluginsConfig>(_configLocation, "PluginsConfig.json");
        SecurityConfig = _loader.LoadSecurityConfig(_configLocation);

        _configs["AppConfig"] = AppConfig;
        _configs["PluginsConfig"] = PluginsConfig;
        _configs["SecurityConfig"] = SecurityConfig;

        if (HotReloadEnabled)
        {
            RegisterFileWatcher<AppConfig>("AppConfig.json");
            RegisterFileWatcher<PluginsConfig>("PluginsConfig.json");
            RegisterFileWatcher<SecurityConfig>("SecurityConfig.json");
        }
    }

    /// <summary>
    /// Registers a file watcher for a config file to enable hot-reload
    /// </summary>
    private void RegisterFileWatcher<T>(string fileName) where T : class, new()
    {
        var watcherName = $"ConfigFileWatcher_{typeof(T).Name}";
        var path = Path.Combine(_configLocation!, fileName);

        if (_fileWatchers.ContainsKey(watcherName))
            return;

        var directory = Path.GetDirectoryName(path);
        var filter = Path.GetFileName(path);

        if (string.IsNullOrEmpty(directory))
            return;

        var watcher = new FileSystemWatcher(directory)
        {
            Filter = filter,
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        watcher.Changed += (sender, args) =>
        {
            if (_exceptCounts.TryGetValue(watcherName, out var count) && count > 0)
            {
                _exceptCounts[watcherName] = count - 1;
                Log.Debug("FileWatcher {WatcherName}: Skipping change event (ExceptCount: {Count})", watcherName, _exceptCounts[watcherName]);
                return;
            }

            Log.Information("FileWatcher {WatcherName}: File changed - {FileName}, {ChangeType}",
                watcherName, args.Name, args.ChangeType);

            try
            {
                ReloadConfigFile<T>(fileName);
                OnConfigChanged(typeof(T).Name, "FileChanged", null, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "FileWatcher {WatcherName}: Error reloading config: {Message}", watcherName, ex.Message);
            }
        };

        _fileWatchers[watcherName] = watcher;
        _exceptCounts[watcherName] = 0;

        Log.Information("FileWatcher {WatcherName}: Registered for {Path}", watcherName, path);
    }

    /// <summary>
    /// Reloads a single config file from disk
    /// </summary>
    private void ReloadConfigFile<T>(string fileName) where T : class, new()
    {
        var path = Path.Combine(_configLocation!, fileName);

        if (!File.Exists(path))
        {
            Log.Warning("Config file {FileName} not found for reload", fileName);
            return;
        }

        object? config = typeof(T) == typeof(SecurityConfig)
            ? _loader.LoadSecurityConfig(_configLocation)
            : _loader.Load<T>(_configLocation, fileName);

        if (config != null)
        {
            _configs[typeof(T).Name] = config;
            ApplyConfig(config);
            Log.Information("Reloaded config file {FileName}", fileName);
        }
    }

    /// <summary>
    /// Increases the exception count to prevent file change events from triggering reloads
    /// </summary>
    public void IncreaseExceptCount(string watcherName, int count = 1)
    {
        if (_exceptCounts.TryGetValue(watcherName, out var current))
            _exceptCounts[watcherName] = current + count;
        else
            _exceptCounts[watcherName] = count;
    }

    /// <summary>
    /// Decreases the exception count
    /// </summary>
    public void DecreaseExceptCount(string watcherName, int count = 1)
    {
        if (_exceptCounts.TryGetValue(watcherName, out var current))
            _exceptCounts[watcherName] = Math.Max(0, current - count);
    }

    /// <summary>
    /// Saves all configurations to files
    /// </summary>
    public void SaveAll()
    {
        var watcherName = "ConfigFileWatcher_AppConfig";
        IncreaseExceptCount(watcherName, 2);
        _saver.Save(AppConfig, _configLocation!, "AppConfig.json");

        watcherName = "ConfigFileWatcher_PluginsConfig";
        IncreaseExceptCount(watcherName, 2);
        _saver.Save(PluginsConfig, _configLocation!, "PluginsConfig.json");

        watcherName = "ConfigFileWatcher_SecurityConfig";
        IncreaseExceptCount(watcherName, 2);
        _saver.Save(SecurityConfig, _configLocation!, "SecurityConfig.json");
    }

    /// <summary>
    /// Reloads all configurations from files
    /// </summary>
    public void Reload()
    {
        Load();
    }

    /// <summary>
    /// Updates the public config properties based on the config object's type
    /// </summary>
    private void ApplyConfig(object config)
    {
        if (config is IAppConfig appConfig)
            AppConfig = appConfig;
        else if (config is IPluginsConfig pluginsConfig)
            PluginsConfig = pluginsConfig;
        else if (config is ISecurityConfig securityConfig)
            SecurityConfig = securityConfig;
    }

    /// <summary>
    /// Raises the config changed event
    /// </summary>
    protected void OnConfigChanged(string configType, string propertyName, object? oldValue = null, object? newValue = null)
    {
        ConfigChanged?.Invoke(this, new ConfigChangedEventArgs
        {
            ConfigType = configType,
            PropertyName = propertyName,
            OldValue = oldValue,
            NewValue = newValue
        });
    }

    /// <summary>
    /// Disposes the configuration manager and releases all resources
    /// </summary>
    public void Dispose()
    {
        foreach (var watcher in _fileWatchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _fileWatchers.Clear();
        _exceptCounts.Clear();

        Log.Information("ConfigManager disposed");
    }
}
