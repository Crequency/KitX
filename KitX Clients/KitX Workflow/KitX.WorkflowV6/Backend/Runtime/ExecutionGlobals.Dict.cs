namespace KitX.WorkflowV6.Backend.Runtime;

using System.Text.Json;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.Dict — the Dict function family (9 functions) plus the private
// AsDict / JsonElementToObject helpers. Dict is KScript's first-class mutable
// key-value container (Dictionary<string, object?>), distinct from JSON (read-only
// JsonElement). Partial of ExecutionGlobals (see ExecutionGlobals.cs).
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    /// <summary>
    /// Normalises an arbitrary runtime value into a Dictionary&lt;string, object?&gt;.
    /// A Dictionary passes through (same reference — in-place mutation works); a JsonElement
    /// object is materialised into a new Dictionary; null/other become an empty dictionary.
    /// </summary>
    private static Dictionary<string, object?> AsDict(object? value)
    {
        if (value is Dictionary<string, object?> d) return d;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, object?>();
            foreach (var prop in je.EnumerateObject())
                result[prop.Name] = JsonElementToObject(prop.Value);
            return result;
        }
        return new Dictionary<string, object?>();
    }

    /// <summary>Converts a JsonElement scalar to its boxed .NET value (for JsonToDict).</summary>
    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt32(out var i) ? i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText(),
    };

    /// <summary>DictGetValue: gets a value by key, or null if absent.</summary>
    public object? DictGetValue(object? dict, string key)
    {
        var d = AsDict(dict);
        return d.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>DictSetValue: sets key→value in place, returns the same Dict.</summary>
    public Dictionary<string, object?> DictSetValue(object? dict, string key, object? value)
    {
        var d = AsDict(dict);
        d[key] = value;
        return d;
    }

    /// <summary>DictGetValues: batch lookup by a JSON key array; missing keys → null.</summary>
    public JsonElement DictGetValues(object? dict, object? keys)
    {
        var d = AsDict(dict);
        var keyArr = AsJsonElement(keys);
        var result = new List<object?>();
        if (keyArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in keyArr.EnumerateArray())
            {
                var k = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                result.Add(k is not null && d.TryGetValue(k, out var v) ? v : null);
            }
        }
        return JsonSerializer.SerializeToElement(result);
    }

    /// <summary>DictMerge: merges source into target in place, returns target.</summary>
    public Dictionary<string, object?> DictMerge(object? target, object? source)
    {
        var t = AsDict(target);
        var s = AsDict(source);
        foreach (var kv in s)
            t[kv.Key] = kv.Value;
        return t;
    }

    /// <summary>DictContainsKey: checks whether a key exists.</summary>
    public bool DictContainsKey(object? dict, string key)
    {
        return AsDict(dict).ContainsKey(key);
    }

    /// <summary>DictKeys: returns all keys as a JSON string array.</summary>
    public JsonElement DictKeys(object? dict)
    {
        return JsonSerializer.SerializeToElement(AsDict(dict).Keys);
    }

    /// <summary>DictRemove: removes a key in place, returns the same Dict.</summary>
    public Dictionary<string, object?> DictRemove(object? dict, string key)
    {
        var d = AsDict(dict);
        d.Remove(key);
        return d;
    }

    /// <summary>DictToJson: Dict → JsonElement bridge (for passing to plugins).</summary>
    public JsonElement DictToJson(object? dict)
    {
        return JsonSerializer.SerializeToElement(AsDict(dict));
    }

    /// <summary>JsonToDict: JsonElement object → Dict bridge (mutable copy).</summary>
    public Dictionary<string, object?> JsonToDict(object? json)
    {
        return AsDict(AsJsonElement(json));
    }
}
