using System;
using System.Collections.Generic;
using System.IO;
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

        // Diagnostic trail — always written, bypasses Serilog
        var diagPath = Path.Combine(Path.GetDirectoryName(fullPath) ?? ".", "ConfigLoadTrail.log");
        var fInfo = new FileInfo(fullPath);
        File.AppendAllText(diagPath, $"[{DateTime.Now:O}] Load<{typeof(T).Name}> path={fullPath}, exists={(fInfo.Exists ? "True" : "False")}, size={(fInfo.Exists ? fInfo.Length.ToString() : "n/a")}, lastWrite={(fInfo.Exists ? fInfo.LastWriteTime.ToString("O") : "n/a")}\n");

        if (!File.Exists(fullPath))
        {
            File.AppendAllText(diagPath, $"[{DateTime.Now:O}] Load<{typeof(T).Name}> FILE NOT FOUND, returning default\n");
            return new T();
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var config = JsonSerializer.Deserialize<T>(json, ConfigSerializationOptions.Options);
            if (config == null)
            {
                File.AppendAllText(diagPath, $"[{DateTime.Now:O}] Load<{typeof(T).Name}> Deserialize returned NULL, returning default\n");
                return new T();
            }
            if (typeof(T) == typeof(AppConfig))
            {
                var ac = (AppConfig)(object)config;

                // Snapshot: parse JSON directly to see what the file really says
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

                File.AppendAllText(diagPath, $"[{DateTime.Now:O}] Load<AppConfig> JSON: LogLevel={jLogLevel}, HomePane={jHomePane}, HomeSelView={jHomeSelView}\n");
                File.AppendAllText(diagPath, $"[{DateTime.Now:O}] Load<AppConfig> OBJ:  LogLevel={(int)ac.Log.LogLevel}, HomePane={(ac.Pages.Home.IsNavigationViewPaneOpened ? "open" : "closed")}, HomeSelView={ac.Pages.Home.SelectedViewName}\n");
            }
            File.AppendAllText(diagPath, $"[{DateTime.Now:O}] Load<{typeof(T).Name}> SUCCESS, json={json.Length} bytes\n");
            return config;
        }
        catch (Exception ex)
        {
            File.AppendAllText(diagPath, $"[{DateTime.Now:O}] Load<{typeof(T).Name}> EXCEPTION: {ex.GetType().Name}: {ex.Message}\nInner: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}\nJSON preview: {(File.Exists(fullPath) ? File.ReadAllText(fullPath)[..Math.Min(500, (int)new FileInfo(fullPath).Length)] : "FILE NOT FOUND")}\n");
            return new T();
        }
    }

    /// <inheritdoc/>
    public ISecurityConfig LoadSecurityConfig(string location)
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

    private static ISecurityConfig DeserializeSecurityConfig(string json)
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
