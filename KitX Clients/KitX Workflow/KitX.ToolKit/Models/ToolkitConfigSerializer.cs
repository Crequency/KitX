using System.Text.Json;

namespace KitX.ToolKit.Models;

/// <summary>
/// System.Text.Json (de)serialization for <see cref="Toolkit"/> config documents.
/// Keeps the JSON human-readable (indented) and case-insensitive so configs authored
/// by hand or by an AI Agent round-trip losslessly. Enums are serialized as strings.
/// </summary>
public static class ToolkitConfigSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Serializes a <see cref="Toolkit"/> to a pretty-printed JSON document.</summary>
    public static string Serialize(Toolkit toolkit)
        => JsonSerializer.Serialize(toolkit, _options);

    /// <summary>Deserializes a <see cref="Toolkit"/> from a JSON document. Returns null on malformed JSON.</summary>
    public static Toolkit? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Toolkit>(json, _options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
