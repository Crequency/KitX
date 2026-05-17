using System.Text.Json;

namespace KitX.Core.Configuration;

/// <summary>
/// JSON serializer options for configuration files
/// </summary>
internal static class ConfigSerializationOptions
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
