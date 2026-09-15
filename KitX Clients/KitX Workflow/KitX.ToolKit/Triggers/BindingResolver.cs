using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// Resolves a binding's <c>Params</c> map into a constant-override dictionary suitable
/// for <c>WorkflowOverrides.ApplyConstantOverrides</c> (Bench RFC §7.3). Each param value
/// is one of:
/// <list type="bullet">
///   <item><c>"$payload.path"</c> — read from the trigger/data packet JSON (dotted path, array index support).</item>
///   <item><c>"$output.path"</c> — read from the predecessor's output packet (same packet for a completion edge).</item>
///   <item>any literal — passed through verbatim.</item>
/// </list>
/// The referenced JSON value is rendered to a string (scalars as-is, objects/arrays compact JSON).
/// </summary>
public static class BindingResolver
{
    private const string PayloadPrefix = "$payload.";
    private const string OutputPrefix = "$output.";
    private static readonly Regex _segment = new(@"[^\[\].]+|\[(\d+)\]", RegexOptions.Compiled);

    // F6: precompiled path→segment index so the per-param regex match + int.Parse runs at
    // most once per distinct path. Paths come from the ToolKit's binding config, which is
    // fixed at mount time, so the key space is bounded by config — an unbounded cache is safe.
    private static readonly ConcurrentDictionary<string, Segment[]> _compiled = new(StringComparer.Ordinal);

    /// <summary>Resolves params against a JSON packet (source payload or predecessor output).</summary>
    public static IReadOnlyDictionary<string, string?> Resolve(
        IReadOnlyDictionary<string, string?>? params_, JsonElement packet)
    {
        if (params_ is null || params_.Count == 0)
            return new Dictionary<string, string?>();

        var result = new Dictionary<string, string?>(params_.Count);
        foreach (var (name, value) in params_)
            result[name] = ResolveValue(value, packet);
        return result;
    }

    private static string? ResolveValue(string? value, JsonElement packet)
    {
        if (value is null)
            return null;

        if (value.StartsWith(PayloadPrefix, StringComparison.OrdinalIgnoreCase))
            return Extract(packet, value[PayloadPrefix.Length..]);

        if (value.StartsWith(OutputPrefix, StringComparison.OrdinalIgnoreCase))
            return Extract(packet, value[OutputPrefix.Length..]);

        return value; // literal
    }

    /// <summary>Navigates a dotted/bracket path and renders the referenced element to a string.</summary>
    private static string? Extract(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var current = root;
        var segments = _compiled.GetOrAdd(path, static p => Compile(p));

        foreach (var segment in segments)
        {
            if (segment.Kind == SegmentKind.Index)
            {
                // Array index segment: [n]
                if (!current.TryGetIntIndex(segment.Index, out var element))
                    return null;
                current = element;
            }
            else
            {
                // Property name segment
                var prop = segment.Name!;
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(prop, out var element))
                    return null;
                current = element;
            }
        }

        return Render(current);
    }

    /// <summary>Parses a path into an ordered segment array (mirrors the original regex semantics exactly).</summary>
    private static Segment[] Compile(string path)
    {
        var matches = _segment.Matches(path);
        var segments = new Segment[matches.Count];
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            segments[i] = match.Groups[1].Success
                ? new Segment(SegmentKind.Index, null, int.Parse(match.Groups[1].Value))
                : new Segment(SegmentKind.Property, match.Value, 0);
        }
        return segments;
    }

    /// <summary>A single path navigation step: either a property name or an array index.</summary>
    private readonly record struct Segment(SegmentKind Kind, string? Name, int Index);

    private enum SegmentKind { Property, Index }

    private static bool TryGetIntIndex(this JsonElement element, int index, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Array &&
            index >= 0 && index < element.GetArrayLength())
        {
            value = element[index];
            return true;
        }
        value = default;
        return false;
    }

    private static string? Render(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => element.GetRawText(), // object / array → compact JSON
    };
}
