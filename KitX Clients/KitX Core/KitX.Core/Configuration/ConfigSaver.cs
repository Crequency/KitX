using System.Text.Json;
using KitX.Core.Contract.Configuration;
using Serilog;

namespace KitX.Core.Configuration;

/// <summary>
/// Saves configuration to files
/// </summary>
public class ConfigSaver : IConfigSaver
{
    /// <inheritdoc/>
    public void Save<T>(T config, string location, string fileName) where T : class
    {
        try
        {
            var path = Path.Combine(location, fileName);

            // Update metadata fields before serialization
            if (config is IConfigWithMetadata metadata)
            {
                var watcherName = $"ConfigFileWatcher_{typeof(T).Name}";
                metadata.ConfigFileLocation = path;
                metadata.ConfigFileWatcherName = watcherName;
                metadata.ConfigGeneratedTime = DateTime.Now;
            }

            var jsonContent = JsonSerializer.Serialize(config, ConfigSerializationOptions.Options);
            var fullPath = Path.GetFullPath(path);
            File.WriteAllText(fullPath, jsonContent);

            Log.Information("[ConfigSaver] Saved {FileName} → {FullPath} ({Bytes} bytes)", fileName, fullPath, jsonContent.Length);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving config file {FileName}: {Message}", fileName, ex.Message);
        }
    }
}
