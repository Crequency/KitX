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
        var path = Path.Combine(location, fileName);

        if (!File.Exists(path))
        {
            Log.Warning("Config file {FileName} not found, creating default", fileName);
            return new T();
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<T>(json, ConfigSerializationOptions.Options);
            return config ?? new T();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading config file {FileName}: {Message}", fileName, ex.Message);
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
