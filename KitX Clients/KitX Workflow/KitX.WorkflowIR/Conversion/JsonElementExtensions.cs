using System.Text.Json;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Shared JSON helpers for builtins and the plugin-call return path
/// (Package/List-Port-And-Json-Functions-Design.md §4.8).
///
/// The runtime hands plugin returns to the workflow as a <see cref="JsonElement"/>
/// (boxed as object), but the type system historically treated them as <c>object</c>.
/// These helpers normalize "anything JSON-shaped" (already a JsonElement, a JSON
/// string, or a serializable object) into a JsonElement so the JSON builtin family
/// can consume plugin returns and string literals uniformly.
/// </summary>
public static class JsonElementExtensions
{
    /// <summary>Coerce a value (object?) into a JsonElement. Accepts:
    /// <list type="bullet">
    ///   <item>An existing <see cref="JsonElement"/> (re-deserialized to a fully owned copy,
    ///       because the source may reference a disposable JsonDocument).</item>
    ///   <item>A JSON <see cref="string"/> (parsed).</item>
    ///   <item>Any other object (serialized to JSON then re-read).</item>
    /// </list>
    /// Returns a <see cref="JsonElement"/> with <see cref="JsonValueKind.Null"/> on failure.</summary>
    public static JsonElement AsJsonElement(this object? value)
    {
        switch (value)
        {
            case null:
                return default;
            case JsonElement je:
                // JsonElement may reference a disposed JsonDocument; re-deserialize to a fully
                // owned copy (Deserialize<JsonElement> produces a detached JsonElement whose
                // backing buffer is self-contained). Clone() alone does NOT detach from the
                // parent JsonDocument and throws InvalidOperationException after disposal.
                return JsonSerializer.Deserialize<JsonElement>(je.GetRawText());
            case string s:
                if (string.IsNullOrWhiteSpace(s)) return default;
                try { return JsonSerializer.Deserialize<JsonElement>(s); }
                catch { return default; }
            default:
                try { return JsonSerializer.SerializeToElement(value); }
                catch { return default; }
        }
    }

    /// <summary>True if the value is a non-null JSON element.</summary>
    public static bool HasValue(this JsonElement el) => el.ValueKind != JsonValueKind.Undefined && el.ValueKind != JsonValueKind.Null;
}
