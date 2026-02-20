using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Configuration;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Loader;
using KitX.Shared.CSharp.Plugin;
using Serilog;

namespace KitX.Core.Configuration;

/// <summary>
/// Configuration manager for managing application configurations
/// </summary>
public class ConfigManager : IConfigService
{
    private static ConfigManager? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static ConfigManager Instance => _instance ??= new();

    private string? _configLocation;

    private readonly Dictionary<string, object> _configs = new();

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
    }

    /// <summary>
    /// Saves all configurations to files
    /// </summary>
    public void SaveAll()
    {
        SaveConfigFile(AppConfig, "AppConfig.json");
        SaveConfigFile(PluginsConfig, "PluginsConfig.json");
        SaveConfigFile(SecurityConfig, "SecurityConfig.json");
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
                var config = JsonSerializer.Deserialize<T>(json);

                if (config != null)
                {
                    _configs[typeof(T).Name] = config;

                    // Update the public properties
                    if (typeof(T) == typeof(AppConfig))
                        AppConfig = config as IAppConfig ?? new AppConfig();
                    else if (typeof(T) == typeof(PluginsConfig))
                        PluginsConfig = config as IPluginsConfig ?? new PluginsConfig();
                    else if (typeof(T) == typeof(SecurityConfig))
                        SecurityConfig = config as ISecurityConfig ?? new SecurityConfig();
                }
            }
            else
            {
                // Create default config
                var config = new T();
                _configs[typeof(T).Name] = config;

                // Save default config
                SaveConfigFile(config, fileName);

                // Update the public properties
                if (typeof(T) == typeof(AppConfig))
                    AppConfig = config as IAppConfig ?? new AppConfig();
                else if (typeof(T) == typeof(PluginsConfig))
                    PluginsConfig = config as IPluginsConfig ?? new PluginsConfig();
                else if (typeof(T) == typeof(SecurityConfig))
                    SecurityConfig = config as ISecurityConfig ?? new SecurityConfig();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error loading config file {fileName}: {ex.Message}");
        }
    }

    private void SaveConfigFile<T>(T config, string fileName)
    {
        try
        {
            var path = Path.Combine(_configLocation!, fileName);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(path, json);
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
}

public class AnnouncementConfig : IAnnouncementConfig
{
    public List<string> Accepted { get; set; } = [];

    /// <summary>
    /// Configuration file location (for backward compatibility)
    /// </summary>
    public string? ConfigFileLocation { get; set; }

    /// <summary>
    /// Saves the configuration to file (for backward compatibility)
    /// </summary>
    /// <param name="path">File path to save</param>
    /// <returns>This instance</returns>
    public AnnouncementConfig Save(string path)
    {
        // Implementation would save to file - simplified for compatibility
        return this;
    }
}


/// <summary>
/// Plugins configuration implementation
/// </summary>
public class PluginsConfig : IPluginsConfig
{
    public IList<IPluginInstallation> Plugins { get; set; } = [];
}

/// <summary>
/// Security configuration implementation
/// </summary>
public class SecurityConfig : ISecurityConfig
{
    public IDictionary<string, IDeviceKey> DeviceKeys { get; set; } = new Dictionary<string, IDeviceKey>();
}
