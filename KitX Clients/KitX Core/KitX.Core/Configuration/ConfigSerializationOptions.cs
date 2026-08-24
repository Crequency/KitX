using System.Text.Json;

namespace KitX.Core.Configuration;

/// <summary>
/// JSON serializer options for configuration files.
/// C-15.8: reuses the shared network-protocol options (WriteIndented +
/// PropertyNameCaseInsensitive superset; IncludeFields/ignore-null are no-ops
/// for config POCOs).
/// </summary>
internal static class ConfigSerializationOptions
{
    internal static readonly JsonSerializerOptions Options = NetworkSerialization.Options;
}
