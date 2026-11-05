using System.Text.Json;
using KitX.ToolKit.Contract;

namespace KitX.ToolKit.Storage;

/// <summary>
/// The durable half of a ToolKit's lifecycle state (design §4, Q12): which lifecycle state it
/// was left in, and what values its declared keys held.
/// </summary>
/// <remarks>
/// <para><b>Why a separate file from <c>toolkit.json</c>.</b> <c>toolkit.json</c> is the
/// <i>declaration</i> — what the toolkit is. This file is <i>runtime state</i> — what happened
/// to it. They have different owners (the user edits the first, the runtime writes the second),
/// different failure consequences (a corrupt declaration must not be silently replaced by a
/// default; a corrupt state file falls back to "as if freshly installed"), and different
/// merge stories if a toolkit is copied between machines. Keeping them apart is what makes the
/// second one disposable.</para>
///
/// <para><b>Why values live here and not in a second storage engine.</b> Design §4.2 (Q66) put
/// the key set under the ToolKit, with <c>DataStore</c> as its implementation. A file next to
/// the declaration is that same idea with the smallest possible mechanism: no new component,
/// no new lifecycle, no second source of truth about what a key is.</para>
///
/// <para><b>What is deliberately not here.</b> Flow state (design Q12: flow state is memory
/// only — a flow does not survive its own process), tombstone records (design §5.5.3.1: a
/// tombstone lives only until the next write, so persisting it would make it permanent), and
/// anything derived (<c>LifecycleState.Installed</c> is inferred from the directory existing,
/// not stored).</para>
/// </remarks>
internal sealed class ToolkitStateFile
{
    /// <summary>The per-ToolKit state file name, at <c>{root}/{id}/state.json</c>.</summary>
    public const string FileName = "state.json";

    /// <summary>
    /// The lifecycle state the toolkit was left in. <see cref="LifecycleState.Installed"/> is
    /// never written: not having this file already means "never mounted".
    /// </summary>
    public LifecycleState? State { get; init; }

    /// <summary>Declared key values, by <i>local</i> name (no toolkit prefix — the directory is
    /// the prefix, so moving a toolkit does not rewrite its keys).</summary>
    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(),
        },
    };

    /// <summary>Serialises to the on-disk form.</summary>
    public string Serialize()
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["state"] = State,
            ["values"] = Values,
        };
        return JsonSerializer.Serialize(payload, Options);
    }

    /// <summary>
    /// Parses the on-disk form. Returns null when the text is not a state file at all.
    /// </summary>
    /// <remarks>
    /// A malformed state file is treated as absent rather than fatal. Nothing in it is a
    /// declaration — losing it costs the last-written values, not the toolkit. The alternative
    /// (refusing to load the toolkit) would make an unreadable cache block the product.
    /// </remarks>
    public static ToolkitStateFile? Deserialize(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            LifecycleState? state = null;
            if (root.TryGetProperty("state", out var stateElement) &&
                stateElement.ValueKind == JsonValueKind.String &&
                Enum.TryParse<LifecycleState>(stateElement.GetString(), ignoreCase: true, out var parsed))
            {
                state = parsed;
            }

            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("values", out var valuesElement) &&
                valuesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in valuesElement.EnumerateObject())
                    values[property.Name] = property.Value.Clone();
            }

            return new ToolkitStateFile { State = state, Values = values };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
