using System.Text.Json;
using KitX.Core.Contract.Configuration;
using KitX.Shared.CSharp.Device;
using Serilog;

namespace KitX.Core.Configuration;

/// <summary>
/// Loads configuration from files
/// </summary>
public class ConfigLoader : IConfigLoader
{
    /// <inheritdoc/>
    public T Load<T>(string location, string fileName) where T : class, new()
    {
        var rawPath = Path.Combine(location, fileName);
        var fullPath = Path.GetFullPath(rawPath);
        var fInfo = new FileInfo(fullPath);

        Log.Debug("[ConfigLoader] Load<{TypeName}> path={Path}, exists={Exists}, size={Size}, lastWrite={LastWrite}",
            typeof(T).Name, fullPath, fInfo.Exists, fInfo.Exists ? fInfo.Length : -1,
            fInfo.Exists ? fInfo.LastWriteTime.ToString("O") : "n/a");

        if (!File.Exists(fullPath))
        {
            Log.Debug("[ConfigLoader] Load<{TypeName}> FILE NOT FOUND, returning default", typeof(T).Name);
            return new T();
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var config = JsonSerializer.Deserialize<T>(json, ConfigSerializationOptions.Options);
            if (config == null)
            {
                Log.Debug("[ConfigLoader] Load<{TypeName}> Deserialize returned NULL, returning default", typeof(T).Name);
                return new T();
            }
            if (typeof(T) == typeof(AppConfig))
            {
                var ac = (AppConfig)(object)config;

                // Diagnostic: parse JSON directly to see what the file really says
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string jLogLevel = "?", jHomePane = "?", jHomeSelView = "?";
                if (root.TryGetProperty("Log", out var jLog) && jLog.TryGetProperty("LogLevel", out var jLevel))
                    jLogLevel = jLevel.GetInt32().ToString();
                if (root.TryGetProperty("Pages", out var jPages) && jPages.TryGetProperty("Home", out var jHome))
                {
                    if (jHome.TryGetProperty("IsNavigationViewPaneOpened", out var jOpen))
                        jHomePane = jOpen.GetBoolean() ? "open" : "closed";
                    if (jHome.TryGetProperty("SelectedViewName", out var jSvn))
                        jHomeSelView = jSvn.GetString() ?? "null";
                }

                Log.Debug("[ConfigLoader] Load<AppConfig> JSON: LogLevel={JsonLevel}, HomePane={JsonHomePane}, HomeSelView={JsonHomeSelView}",
                    jLogLevel, jHomePane, jHomeSelView);
                Log.Debug("[ConfigLoader] Load<AppConfig> OBJ:  LogLevel={ObjLevel}, HomePane={ObjHomePane}, HomeSelView={ObjHomeSelView}",
                    (int)ac.Log.LogLevel, ac.Pages.Home.IsNavigationViewPaneOpened ? "open" : "closed",
                    ac.Pages.Home.SelectedViewName);
            }
            Log.Debug("[ConfigLoader] Load<{TypeName}> SUCCESS, json={JsonLength} bytes", typeof(T).Name, json.Length);
            return config;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ConfigLoader] Load<{TypeName}> EXCEPTION: {Message}", typeof(T).Name, ex.Message);
            return new T();
        }
    }

    /// <inheritdoc/>
    public ISecurityConf LoadSecurityConfig(string location)
    {
        var path = Path.Combine(location, "SecurityConfig.json");

        if (!File.Exists(path))
        {
            Log.Warning("SecurityConfig.json not found, creating default");
            return new SecurityConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            return DeserializeSecurityConfig(json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading SecurityConfig: {Message}", ex.Message);
            return new SecurityConfig();
        }
    }

    private static ISecurityConf DeserializeSecurityConfig(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var config = new SecurityConfig();

            if (root.TryGetProperty("ConfigFileLocation", out var configFileLocation))
                config.ConfigFileLocation = configFileLocation.GetString();
            if (root.TryGetProperty("ConfigFileWatcherName", out var configFileWatcherName))
                config.ConfigFileWatcherName = configFileWatcherName.GetString();
            if (root.TryGetProperty("ConfigGeneratedTime", out var configGeneratedTime))
                if (DateTime.TryParse(configGeneratedTime.GetString(), out var generatedTime))
                    config.ConfigGeneratedTime = generatedTime;

            if (root.TryGetProperty("DeviceKeys", out var deviceKeysElement))
            {
                var deviceKeys = new List<DeviceKeyImpl>();

                foreach (var keyElement in deviceKeysElement.EnumerateArray())
                {
                    var impl = new DeviceKeyImpl();

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

                    impl.RsaPublicKeyPem = keyElement.TryGetProperty("RsaPublicKeyPem", out var pubKey) ? pubKey.GetString() : null;
                    impl.RsaPrivateKeyPem = keyElement.TryGetProperty("RsaPrivateKeyPem", out var privKey) ? privKey.GetString() : null;

                    if (keyElement.TryGetProperty("AddedAt", out var addedAtElement))
                        if (DateTime.TryParse(addedAtElement.GetString(), out var addedAt))
                            impl.AddedAt = addedAt;

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
}
