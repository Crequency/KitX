namespace KitX.WorkflowV6.Backend.Runtime;

using System.Text.Json;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.Json — the JSON function family (7 functions) plus the private
// AsJsonElement / TryParseJson normalisers. Plugin communication is JSON-based
// (WebSocket Command.Body); PluginCall returns are normalised to JsonElement via
// AsJsonElement. Partial of ExecutionGlobals (see ExecutionGlobals.cs).
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    /// <summary>
    /// Normalises an arbitrary runtime value into a JsonElement. JsonElement passes
    /// through; JSON strings are parsed; non-JSON strings are wrapped as JSON string
    /// values; other objects are serialised.
    /// </summary>
    private static JsonElement AsJsonElement(object? value) => value switch
    {
        JsonElement je => je,
        null => default,
        string s => TryParseJson(s, out var parsed) ? parsed : JsonSerializer.SerializeToElement(s),
        _ => JsonSerializer.SerializeToElement(value),
    };

    private static bool TryParseJson(string s, out JsonElement result)
    {
        try { result = JsonSerializer.Deserialize<JsonElement>(s); return true; }
        catch (JsonException) { result = default; return false; }
    }

    /// <summary>
    /// Enumerates a forEach collection source at runtime: JSON arrays yield their
    /// elements, other IEnumerables pass through, anything else (null, scalars,
    /// JSON objects) yields nothing. Codegen routes JsonElement- and object?-typed
    /// forEach sources through this helper because JsonElement does not implement
    /// IEnumerable and cannot appear directly in a C# foreach.
    /// </summary>
    public IEnumerable<object?> Enumerate(object? source)
    {
        switch (source)
        {
            case null:
                break;
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                foreach (var element in array.EnumerateArray())
                    yield return element;
                break;
            case string text:
                foreach (var ch in text)
                    yield return ch.ToString();
                break;
            case System.Collections.IEnumerable enumerable:
                foreach (var item in enumerable)
                    yield return item;
                break;
        }
    }

    /// <summary>JsonAsString: extracts a string from a JSON value. Undefined (null/empty)
    /// yields an empty string — GetRawText() would throw on ValueKind.Undefined.</summary>
    public string JsonAsString(object? json)
    {
        var je = AsJsonElement(json);
        if (je.ValueKind == JsonValueKind.Undefined) return "";
        return je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.GetRawText();
    }

    /// <summary>JsonAsInt: extracts an integer from a JSON value. Non-integral numbers
    /// truncate toward zero (matching <see cref="JsonElementToObject"/>'s int fallback);
    /// non-numbers yield 0.</summary>
    public int JsonAsInt(object? json)
    {
        var je = AsJsonElement(json);
        if (je.ValueKind != JsonValueKind.Number) return 0;
        return je.TryGetInt32(out var i) ? i : (int)je.GetDouble();
    }

    /// <summary>JsonAsBool: extracts a boolean from a JSON value.</summary>
    public bool JsonAsBool(object? json)
    {
        var je = AsJsonElement(json);
        return je.ValueKind == JsonValueKind.True;
    }

    /// <summary>JsonArrayAt: gets the element at a zero-based index from a JSON array.</summary>
    public JsonElement JsonArrayAt(object? json, int index)
    {
        var je = AsJsonElement(json);
        if (je.ValueKind != JsonValueKind.Array) return default;
        int i = 0;
        foreach (var element in je.EnumerateArray())
        {
            if (i == index) return element;
            i++;
        }
        return default;
    }

    /// <summary>JsonObjectKeys: gets the key names of a JSON object as a JSON string array.</summary>
    public JsonElement JsonObjectKeys(object? json)
    {
        var je = AsJsonElement(json);
        if (je.ValueKind != JsonValueKind.Object) return default;
        var keys = je.EnumerateObject().Select(p => p.Name);
        return JsonSerializer.SerializeToElement(keys);
    }

    /// <summary>JsonGetField: traverses a JSON object by dotted path and returns the value.</summary>
    public JsonElement JsonGetField(object? json, string fieldPath)
        => TryGetPropertyPath(AsJsonElement(json), fieldPath, out var result) ? result : default;

    /// <summary>JsonContains: checks whether a dotted path exists in a JSON object.</summary>
    public bool JsonContains(object? json, string path)
        => TryGetPropertyPath(AsJsonElement(json), path, out _);

    /// <summary>
    /// Walks <paramref name="path"/> (dot-separated property names) from <paramref name="root"/>.
    /// Returns false (and leaves <paramref name="result"/> undefined) when any segment is
    /// missing or the traversal hits a non-object. Shared by <see cref="JsonGetField"/> and
    /// <see cref="JsonContains"/>.
    /// </summary>
    private static bool TryGetPropertyPath(JsonElement root, string path, out JsonElement result)
    {
        result = root;
        foreach (var part in path.Split('.'))
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(part, out result))
                return false;
        }
        return true;
    }
}
