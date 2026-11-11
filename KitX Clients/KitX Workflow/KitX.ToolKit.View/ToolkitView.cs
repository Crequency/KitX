using System.Text.Json;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;

namespace KitX.ToolKit.View;

/// <summary>
/// A View's local projection of one ToolKit: the values it has been told and the card layout.
/// </summary>
/// <remarks>
/// <para><b>It holds nothing the server did not say.</b> Every field here arrives in a message;
/// nothing is computed, defaulted from config, or remembered across a <c>welcome</c>. That is what
/// makes reconnecting a matter of throwing this away and folding a new <c>welcome</c> — there is no
/// client-side truth to reconcile (ADR-0014 D3).</para>
///
/// <para><b>Replacement, never merge — except where the server said to append.</b> A <c>welcome</c>
/// replaces the whole projection and a <c>surface</c> replaces the whole layout. Merging would keep a
/// value the server has since dropped, and the window in which the client shows a stale value is
/// exactly the window nobody looks at it. The cost is a visible flash on reconnect; the alternative
/// is a silent wrong value, which is worse.</para>
///
/// <para><b>Append is the one exception, and it is not a merge.</b> A delta with <c>appended</c> set
/// carries exactly one item <em>to be concatenated</em>; the server is stating "one more of those",
/// not "the new whole". Treating it as a replacement would leave the projection holding the last
/// line of a log the server holds in full, and nothing would ever correct it. So the projection
/// concatenates — from a value that a <c>welcome</c> gave it, which is why the next <c>welcome</c>
/// still overrides everything an append built.</para>
///
/// <para><b>Thread-safe by lock.</b> Messages typically arrive on a transport thread and are read on
/// a UI thread. Making the caller responsible for that works until the first time it is forgotten,
/// and the failure is a torn dictionary read rather than an exception.</para>
/// </remarks>
public sealed class ToolkitView
{
    private readonly object _gate = new();
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private SurfaceDocument _surface = SurfaceDocument.Empty;
    private string? _toolkitId;
    private int _protocolVersion;

    /// <summary>The ToolKit this projection belongs to, or <see langword="null"/> before any <c>welcome</c>.</summary>
    public string? ToolkitId
    {
        get { lock (_gate) return _toolkitId; }
    }

    /// <summary>The protocol version the server answered with.</summary>
    public int ProtocolVersion
    {
        get { lock (_gate) return _protocolVersion; }
    }

    /// <summary>Whether a <c>welcome</c> has been folded in.</summary>
    public bool IsAttached
    {
        get { lock (_gate) return _toolkitId is not null; }
    }

    /// <summary>A copy of the values, safe to hold after the call returns.</summary>
    public IReadOnlyDictionary<string, JsonElement> Values
    {
        get { lock (_gate) return new Dictionary<string, JsonElement>(_values, StringComparer.Ordinal); }
    }

    /// <summary>The card layout. Never <see langword="null"/>; empty until one arrives.</summary>
    public SurfaceDocument Surface
    {
        get { lock (_gate) return _surface; }
    }

    /// <summary>Reads one value, if the projection has it.</summary>
    /// <param name="key">The key's local name.</param>
    /// <param name="value">Its value when present.</param>
    public bool TryGetValue(string key, out JsonElement value)
    {
        lock (_gate)
            return _values.TryGetValue(key, out value);
    }

    /// <summary>
    /// Folds one message into the projection.
    /// </summary>
    /// <param name="message">The message to apply.</param>
    /// <returns>
    /// <see langword="true"/> when the projection changed. Messages that carry no state —
    /// acknowledgements, refusals, the ToolKit list — return <see langword="false"/>: they are
    /// answers to a request and belong to whoever made it, not to a projection.
    /// </returns>
    /// <remarks>
    /// A <c>delta</c> that arrives before any <c>welcome</c> is applied rather than refused. The
    /// server only sends deltas to an attached session, so one arriving here means a message was
    /// delivered out of order; recording it is more useful than dropping it, and the next
    /// <c>welcome</c> will settle the question either way.
    /// </remarks>
    public bool Apply(ViewMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            switch (message)
            {
                case WelcomeMessage welcome:
                    // Whole replacement. Anything the previous session held is discarded, including
                    // keys this one does not mention — "not in welcome" means "has no value".
                    _toolkitId = welcome.ToolkitId;
                    _protocolVersion = welcome.ProtocolVersion;
                    _values.Clear();
                    foreach (var (key, value) in welcome.Values)
                        _values[key] = value;
                    _surface = welcome.Surface ?? SurfaceDocument.Empty;
                    return true;

                case DeltaMessage delta:
                    if (string.IsNullOrEmpty(delta.Key))
                        return false;

                    if (delta.Value is not { } incoming)
                    {
                        // Explicit removal. The message with a null value is how a key stops having
                        // one; the key is still declared, which is why this is not `cleared`.
                        return _values.Remove(delta.Key);
                    }

                    if (delta.Appended)
                    {
                        // One item, not a batch (ViewMessage.DeltaMessage). The projection keeps the
                        // sequence because the sequence *is* the server's value: a welcome states
                        // the whole array, and an append states "one more of those". Treating the
                        // frame as a replacement would leave the projection showing the last line of
                        // a log the server holds in full, and no later message would ever correct
                        // it — the next append only replaces that scalar with the next one.
                        _values.TryGetValue(delta.Key, out var existing);
                        _values[delta.Key] = AppendOne(existing, incoming);
                        return true;
                    }

                    _values[delta.Key] = incoming;
                    return true;

                case SurfaceMessage surface:
                    _surface = surface.Surface ?? SurfaceDocument.Empty;
                    return true;

                case ClearedMessage cleared:
                    if (_toolkitId is not null
                        && !string.Equals(cleared.ToolkitId, _toolkitId, StringComparison.Ordinal))
                    {
                        // Another ToolKit's clear. Sessions are per-ToolKit, so this should not
                        // happen; ignoring it is safer than clearing the wrong projection.
                        return false;
                    }

                    // Values only. The layout is not data and is unaffected by a data clear.
                    if (_values.Count == 0)
                        return false;
                    _values.Clear();
                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>Concatenates one item onto a value, matching the data plane's <c>Append</c>.</summary>
    /// <remarks>
    /// A non-array value is <b>replaced</b> by <c>[item]</c> — the existing scalar is dropped, not
    /// kept. That is <c>DataStore.Append</c>: it copies the old elements into the ring only when the
    /// old value was an array (<c>DataStore.cs:202-206</c>), so appending to a scalar leaves the ring
    /// holding just the new item. Modelling that here rather than throwing keeps the projection
    /// equal to the server's value in the case the server permits.
    /// </remarks>
    private static JsonElement AppendOne(JsonElement? existing, JsonElement item)
    {
        var items = new List<JsonElement>();
        if (existing is { ValueKind: JsonValueKind.Array } array)
            foreach (var element in array.EnumerateArray())
                items.Add(element.Clone());

        items.Add(item.Clone());
        return JsonSerializer.SerializeToElement(items);
    }
}
