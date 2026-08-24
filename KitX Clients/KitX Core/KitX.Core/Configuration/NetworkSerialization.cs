using System.Text.Json;
using System.Text.Json.Serialization;

namespace KitX.Core.Configuration;

/// <summary>
/// C-15.8: single source of truth for the JSON serializer options used by the
/// KitX network protocol (legacy wire format). Previously each of
/// DeviceHttpClient / DevicesServer / PluginsServer / AnnouncementManager /
/// ConfigSerializationOptions declared its own near-identical copy.
/// </summary>
internal static class NetworkSerialization
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
