using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KitX.Core.Contract.Configuration;
using KitX.Shared.CSharp.Device;
using Serilog;

namespace KitX.Core.Configuration;

/// <summary>
/// Configuration manager for managing application configurations
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
    private ConfigManager() { }

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

        LoadConfigFile<AppConfig>("AppConfig.json");
        LoadConfigFile<PluginsConfig>("PluginsConfig.json");
        LoadConfigFile<SecurityConfig>("SecurityConfig.json");

        // Register file watchers for hot-reload if enabled
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
    private void RegisterFileWatcher<T>(string fileName) where T : class
    {
        var watcherName = $"ConfigFileWatcher_{typeof(T).Name}";
        var path = Path.Combine(_configLocation!, fileName);

        if (_fileWatchers.ContainsKey(watcherName))
        {
            // Already registered
            return;
        }

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
            // Use ExceptCount to prevent infinite loops
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
                // Reload the config file directly - read from file and update the stored config
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
    /// Reloads a single config file from disk (used by file watcher)
    /// </summary>
    private void ReloadConfigFile<T>(string fileName) where T : class
    {
        var path = Path.Combine(_configLocation!, fileName);

        if (!File.Exists(path))
        {
            Log.Warning("Config file {FileName} not found for reload", fileName);
            return;
        }

        var json = File.ReadAllText(path);
        object? config = null;

        // Special handling for SecurityConfig to deserialize correctly
        if (typeof(T) == typeof(SecurityConfig))
        {
            config = DeserializeSecurityConfig(json);
        }
        else
        {
            // Use ConfigBase for polymorphic deserialization
            config = JsonSerializer.Deserialize<T>(json, ConfigSerializationOptions.Options);
        }

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
        {
            _exceptCounts[watcherName] = current + count;
        }
        else
        {
            _exceptCounts[watcherName] = count;
        }
    }

    /// <summary>
    /// Decreases the exception count
    /// </summary>
    public void DecreaseExceptCount(string watcherName, int count = 1)
    {
        if (_exceptCounts.TryGetValue(watcherName, out var current))
        {
            _exceptCounts[watcherName] = Math.Max(0, current - count);
        }
    }

    /// <summary>
    /// Saves all configurations to files
    /// </summary>
    public void SaveAll()
    {
        SaveConfigFile((AppConfig)AppConfig, "AppConfig.json");
        SaveConfigFile((PluginsConfig)PluginsConfig, "PluginsConfig.json");
        SaveConfigFile((SecurityConfig)SecurityConfig, "SecurityConfig.json");
    }

    /// <summary>
    /// Deserializes security config from JSON with proper structure
    /// </summary>
    private SecurityConfig DeserializeSecurityConfig(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var config = new SecurityConfig();

            // Parse metadata fields
            if (root.TryGetProperty("ConfigFileLocation", out var configFileLocation))
            {
                config.ConfigFileLocation = configFileLocation.GetString();
            }
            if (root.TryGetProperty("ConfigFileWatcherName", out var configFileWatcherName))
            {
                config.ConfigFileWatcherName = configFileWatcherName.GetString();
            }
            if (root.TryGetProperty("ConfigGeneratedTime", out var configGeneratedTime))
            {
                if (DateTime.TryParse(configGeneratedTime.GetString(), out var generatedTime))
                    config.ConfigGeneratedTime = generatedTime;
            }

            if (root.TryGetProperty("DeviceKeys", out var deviceKeysElement))
            {
                var deviceKeys = new List<DeviceKeyImpl>();

                foreach (var keyElement in deviceKeysElement.EnumerateArray())
                {
                    var impl = new DeviceKeyImpl();

                    // Parse Device object
                    if (keyElement.TryGetProperty("Device", out var deviceElement))
                    {
                        impl.Device = new DeviceLocator
                        {
                            DeviceName = deviceElement.TryGetProperty("DeviceName", out var dn) ? dn.GetString() ?? "" : "",
                            IPv4 = deviceElement.TryGetProperty("IPv4", out var ipv4) ? ipv4.GetString() ?? "" : "",
                            IPv6 = deviceElement.TryGetProperty("IPv6", out var ipv6) ? ipv6.GetString() ?? "" : "",
                            MacAddress = deviceElement.TryGetProperty("MacAddress", out var mac) ? mac.GetString() ?? "" : ""
                        };
                    }

                    // Parse RSA keys
                    impl.RsaPublicKeyPem = keyElement.TryGetProperty("RsaPublicKeyPem", out var pubKey) ? pubKey.GetString() : null;
                    impl.RsaPrivateKeyPem = keyElement.TryGetProperty("RsaPrivateKeyPem", out var privKey) ? privKey.GetString() : null;

                    // Parse AddedAt
                    if (keyElement.TryGetProperty("AddedAt", out var addedAtElement))
                    {
                        if (DateTime.TryParse(addedAtElement.GetString(), out var addedAt))
                            impl.AddedAt = addedAt;
                    }

                    deviceKeys.Add(impl);
                }

                config.DeviceKeys = deviceKeys;
            }

            return config;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deserializing SecurityConfig: {Message}", ex.Message);
            return new SecurityConfig();
        }
    }

    /// <summary>
    /// Reloads all configurations from files
    /// </summary>
    public void Reload()
    {
        Load();
    }

    private void LoadConfigFile<T>(string fileName) where T : class, new()
    {
        try
        {
            var path = Path.Combine(_configLocation!, fileName);

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                object? config = null;

                // Special handling for SecurityConfig to deserialize correctly
                if (typeof(T) == typeof(SecurityConfig))
                {
                    config = DeserializeSecurityConfig(json);
                }
                else
                {
                    // Use ConfigBase for polymorphic deserialization
                    config = JsonSerializer.Deserialize<T>(json, ConfigSerializationOptions.Options);
                }

                if (config != null)
                {
                    _configs[typeof(T).Name] = config;
                    ApplyConfig(config);
                }
            }
            else
            {
                // Create default config
                var config = new T();
                _configs[typeof(T).Name] = config;

                // Save default config
                SaveConfigFile(config, fileName);
                ApplyConfig(config);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error loading config file {fileName}: {ex.Message}");
        }
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
    /// Saves config file
    /// </summary>
    private void SaveConfigFile<T>(T config, string fileName) where T : class
    {
        try
        {
            var path = Path.Combine(_configLocation!, fileName);

            // Increase ExceptCount to prevent file change event from triggering reload
            var watcherName = $"ConfigFileWatcher_{typeof(T).Name}";
            IncreaseExceptCount(watcherName, 2);

            // Update metadata fields before serialization
            if (config is IConfigWithMetadata metadata)
            {
                metadata.ConfigFileLocation = path;
                metadata.ConfigFileWatcherName = watcherName;
                metadata.ConfigGeneratedTime = DateTime.Now;
            }

            // Serialize the config object directly
            var jsonContent = JsonSerializer.Serialize(config, ConfigSerializationOptions.Options);
            File.WriteAllText(path, jsonContent);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error saving config file {fileName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Raises the config changed event
    /// </summary>
    /// <param name="configType">The configuration type</param>
    /// <param name="propertyName">The property name that changed</param>
    /// <param name="oldValue">The old value</param>
    /// <param name="newValue">The new value</param>
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
        // Dispose all file watchers
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
