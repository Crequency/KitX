using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace KitX.ToolKit.Data;

/// <summary>
/// Raised by <see cref="DataStore.Changed"/> when a key is written, removed or appended.
/// Fired outside the store's lock — subscribers must marshal to their own thread (the
/// desktop adapter marshals to the UI thread; the remote bridge filters by subscription).
///
/// <para>For <see cref="DataStore.Append"/> the event is incremental: <see cref="Appended"/>
/// is <c>true</c> and <see cref="NewValue"/> carries the single newly-appended entry rather
/// than the whole ring array. <see cref="Appended"/> defaults to <c>false</c> so existing
/// Set/Remove subscribers are source-compatible.</para>
/// </summary>
internal sealed record DataStoreChangedEventArgs(string Key, JsonElement? OldValue, JsonElement? NewValue, bool Removed, bool Appended = false);

/// <summary>
/// The ToolKit-held in-memory JSON data blackboard (Bench RFC §6). Workflow
/// instances read/write shared keys through it; data outlives the individual run.
///
/// <para>Blocking semantics follow the existing workflow execution model: the runtime
/// executes workflows synchronously, so <see cref="Wait"/> / <see cref="WaitAny"/>
/// block the calling workflow on a <see cref="TaskCompletionSource"/> until the
/// required keys are written (writer side <c>TrySetResult</c>). A timeout bounds the
/// block so a never-arriving writer cannot hang the workflow forever.</para>
///
/// <para>Scoping is purely key-based: callers control it. Explicit <c>DataStore*</c>
/// functions use plain global keys; the Bench scheduler derives instance-scoped edge
/// keys (e.g. <c>{toolkitId}/{instanceId}/{edgeId}</c>) so concurrent trigger paths
/// never pollute each other.</para>
///
/// <para><see cref="Changed"/> is the panel-projection / remote-push primitive: the
/// desktop renderer and (later) the WS bridge subscribe and filter by bound keys.</para>
/// </summary>
internal sealed class DataStore : KitX.ToolKit.Contract.IKeyStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _data = new();
    private readonly object _gate = new();

    // F1: authoritative ring state per array key. Guarded by _gate. _data remains the
    // lazily-materialized external view; a ring is only re-serialized on demand (Get).
    private readonly Dictionary<string, RingState> _rings = new();

    // F3: inverted index from namespace bucket (first two key segments) to the keys in it.
    // Nested ConcurrentDictionary keeps the hot Set/Append path lock-free for indexing.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _nsIndex = new();

    private readonly DataStoreOptions _options;

    public DataStore(DataStoreOptions? options = null)
    {
        _options = options ?? new DataStoreOptions();
    }

    /// <summary>Raised on Set/Remove/Append, outside the store lock. Subscribers marshal themselves.</summary>
    /// <remarks>
    /// This is the <i>internal</i> event, carrying the full change record. The contract's
    /// <see cref="KitX.ToolKit.Contract.IKeyStore.Changed"/> is a separate, narrower event that
    /// carries only the key — a per-change payload would allocate on every write, and the
    /// protocol the View is heading for streams deltas and re-reads rather than values.
    /// </remarks>
    public event EventHandler<DataStoreChangedEventArgs>? Changed;

    /// <summary>
    /// Raised by <see cref="Clear"/> after every key is gone.
    ///
    /// <para>Flows blocked on keys listen to this: a clear makes their condition permanently
    /// unsatisfiable, so releasing them is the only alternative to leaving each one to sit out its
    /// full timeout on data that is provably gone.</para>
    /// </summary>
    public event EventHandler? Cleared;

    /// <inheritdoc/>
    event EventHandler<string>? KitX.ToolKit.Contract.IKeyStore.Changed
    {
        add => _keyChanged += value;
        remove => _keyChanged -= value;
    }

    private event EventHandler<string>? _keyChanged;

    /// <inheritdoc/>
    public bool TryGet(string key, out JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            if (_rings.TryGetValue(key, out var ring) && ring.Dirty)
            {
                _data[key] = Materialize(ring);
                ring.Dirty = false;
            }
        }

        // _data.TryGetValue distinguishes "absent" from "present and JSON null": the latter
        // stores a JsonElement whose ValueKind is Null, so the out value is meaningful even
        // though the payload is null.
        if (_data.TryGetValue(key, out value))
            return true;

        value = default;
        return false;
    }

    /// <inheritdoc/>
    void KitX.ToolKit.Contract.IKeyStore.Set(string key, JsonElement value) => Set(key, value);

    /// <summary>Writes a key. Blocked flows are released by <see cref="Cleared"/> / <see cref="Changed"/> subscribers.</summary>
    /// <param name="value">Any value — normalized to a <see cref="JsonElement"/> (JSON object / array / string / number).</param>
    public void Set(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        var newValue = Normalize(value);
        var isNewKey = !_data.TryGetValue(key, out var oldValue);
        _data[key] = newValue;

        // Keep the ring authoritative state consistent with the external view.
        lock (_gate)
        {
            if (_rings.TryGetValue(key, out var ring))
            {
                if (newValue.ValueKind == JsonValueKind.Array)
                {
                    // Rebuild the ring from the new array (O(n) one-time); _data already
                    // holds the array so the ring is not dirty.
                    ring.Entries.Clear();
                    foreach (var el in newValue.EnumerateArray())
                        ring.Entries.Enqueue(el.Clone());
                    ring.Dirty = false;
                }
                else
                {
                    // Non-array write drops the ring; a later Append reopens [value].
                    _rings.Remove(key);
                }
            }
        }

        if (isNewKey)
            IndexAdd(key);
        Changed?.Invoke(this, new DataStoreChangedEventArgs(key, oldValue, newValue, Removed: false));
        _keyChanged?.Invoke(this, key);
    }

    /// <summary>
    /// Atomically appends a value to an array key. When the key is not an array it is
    /// replaced with <c>[value]</c>. When the array exceeds <paramref name="maxEntries"/>
    /// (or <see cref="DataStoreOptions.AppendLimit"/> when null), the oldest entries are
    /// dropped (ring buffer) — the log control's underlying primitive.
    ///
    /// <para>Append is O(1) amortized: the entry is enqueued into the authoritative ring
    /// and the array is only re-serialized lazily on the next <see cref="Get"/>. The
    /// <see cref="Changed"/> event carries the single new entry (<see cref="DataStoreChangedEventArgs.Appended"/>).</para>
    /// </summary>
    public void Append(string key, object? value, int? maxEntries = null)
    {
        ArgumentNullException.ThrowIfNull(key);

        var limit = maxEntries ?? _options.AppendLimit;
        var newValue = Normalize(value);
        JsonElement? oldValue;
        bool isNewKey;

        lock (_gate)
        {
            isNewKey = !_data.TryGetValue(key, out var existing);
            oldValue = existing;

            bool ringCreated = false;
            if (!_rings.TryGetValue(key, out var ring))
            {
                ring = new RingState();
                _rings[key] = ring;
                ringCreated = true;
                if (existing.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in existing.EnumerateArray())
                        ring.Entries.Enqueue(el.Clone());
                }
            }

            ring.Entries.Enqueue(newValue.Clone());
            if (limit > 0)
            {
                while (ring.Entries.Count > limit)
                    ring.Entries.Dequeue();
            }
            ring.Dirty = true;

            // First append for this key: materialize once so ContainsKey/Keys see the
            // key immediately (and a prior scalar value is replaced by [value]).
            if (ringCreated)
            {
                _data[key] = Materialize(ring);
                ring.Dirty = false;
            }
        }

        if (isNewKey)
            IndexAdd(key);
        Changed?.Invoke(this, new DataStoreChangedEventArgs(key, oldValue, newValue, Removed: false, Appended: true));
        _keyChanged?.Invoke(this, key);
    }

    /// <summary>Synchronous read; null when the key is absent. Lazily materializes a dirty ring.</summary>
    public JsonElement? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            if (_rings.TryGetValue(key, out var ring) && ring.Dirty)
            {
                _data[key] = Materialize(ring);
                ring.Dirty = false;
            }
        }
        return _data.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Removes a key. Returns true when it existed.</summary>
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_data.TryRemove(key, out var old))
            return false;
        lock (_gate)
            _rings.Remove(key);
        IndexRemove(key);
        Changed?.Invoke(this, new DataStoreChangedEventArgs(key, old, null, Removed: true));
            _keyChanged?.Invoke(this, key);
        return true;
    }

    /// <summary>All currently-present keys.</summary>
    public IEnumerable<string> Keys() => _data.Keys;

    /// <summary>True when the key is present.</summary>
    public bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _data.ContainsKey(key);
    }

    /// <summary>
    /// Drops every key. Flows blocked on those keys are released through
    /// <see cref="Cleared"/> — this type no longer knows who is waiting, which is the point
    /// (ADR-0004 D1: waiting is flow scheduling, not storage).
    /// </summary>
    public void Clear()
    {
        _data.Clear();
        _nsIndex.Clear();
        lock (_gate)
            _rings.Clear();

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the keys whose namespace matches <paramref name="prefix"/> (ordinal prefix
    /// match on the full key). Uses the inverted index when the prefix has at least two
    /// segments; falls back to a full scan for shorter prefixes.
    /// </summary>
    public IReadOnlyList<string> KeysByPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (SegmentCount(prefix) < 2)
            return _data.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        return BucketsFor(prefix)
            .SelectMany(set => set.Keys)
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Removes every key whose full key starts with <paramref name="prefix"/> (ordinal).
    /// Fires <see cref="Changed"/> with <c>Removed: true</c> for each removed key, outside
    /// the store lock. Returns the number of keys removed.
    /// </summary>
    public int RemoveByPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        List<string> toRemove;
        if (SegmentCount(prefix) < 2)
        {
            toRemove = _data.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }
        else
        {
            toRemove = BucketsFor(prefix)
                .SelectMany(set => set.Keys)
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
        }

        var removed = 0;
        foreach (var key in toRemove)
        {
            if (_data.TryRemove(key, out var old))
            {
                lock (_gate)
                    _rings.Remove(key);
                IndexRemove(key);
                removed++;
                Changed?.Invoke(this, new DataStoreChangedEventArgs(key, old, null, Removed: true));
            _keyChanged?.Invoke(this, key);
            }
        }
        return removed;
    }


    private static JsonElement Normalize(object? value) => value switch
    {
        null => JsonSerializer.SerializeToElement<object?>(null),
        JsonElement je => je.Clone(),
        JsonDocument jd => jd.RootElement.Clone(),
        _ => JsonSerializer.SerializeToElement(value),
    };

    /// <summary>Materializes a ring's queue into a JSON array element.</summary>
    private static JsonElement Materialize(RingState ring)
    {
        var arr = new JsonArray();
        foreach (var el in ring.Entries)
            arr.Add(JsonNode.Parse(el.GetRawText()));
        return JsonSerializer.SerializeToElement(arr);
    }

    /// <summary>
    /// The namespace bucket for a key: its first two segments (<c>a/b</c>). Keys with no
    /// slash or a single segment map to the whole key itself.
    /// </summary>
    private static string NamespaceBucketOf(string key)
    {
        var first = key.IndexOf('/');
        if (first < 0)
            return key;
        var second = key.IndexOf('/', first + 1);
        return second < 0 ? key : key[..second];
    }

    /// <summary>
    /// Every index bucket whose keys could start with <paramref name="prefix"/>.
    /// </summary>
    /// <remarks>
    /// <para>Buckets are keyed by a key's first two segments, but a prefix does not have to align
    /// with that: <c>"tk-life/"</c> spans the buckets <c>tk-life/status</c>, <c>tk-life/panel</c>
    /// and so on. Mapping the prefix to one bucket (<see cref="NamespaceBucketOf"/>) returned the
    /// literal <c>tk-life/</c>, which never exists as a bucket, so a whole-toolkit sweep silently
    /// found nothing.</para>
    /// <para>A prefix with at least two complete segments (<c>a/b/c</c>) pins the bucket exactly,
    /// which is the case every caller before S4 used. The shorter case is resolved by scanning the
    /// buckets of the one namespace it can name.</para>
    /// </remarks>
    private IEnumerable<ConcurrentDictionary<string, byte>> BucketsFor(string prefix)
    {
        var second = prefix.IndexOf('/', prefix.IndexOf('/') + 1);
        if (second > 0)
        {
            // "a/b/..." — the bucket is fully determined.
            return _nsIndex.TryGetValue(prefix[..second], out var exact)
                ? [exact]
                : [];
        }

        // "a/..." — the namespace segment is known, the rest is not.
        var namespaceSegment = prefix[..prefix.IndexOf('/')];
        return _nsIndex
            .Where(kv => kv.Key.StartsWith(namespaceSegment + "/", StringComparison.Ordinal))
            .Select(kv => kv.Value);
    }

    /// <summary>Number of '/' separated segments in a key (at least 1).</summary>
    private static int SegmentCount(string key)
    {
        var count = 1;
        foreach (var c in key)
            if (c == '/')
                count++;
        return count;
    }

    /// <summary>Adds a key to the inverted index (idempotent).</summary>
    private void IndexAdd(string key)
    {
        var bucket = NamespaceBucketOf(key);
        var set = _nsIndex.GetOrAdd(bucket, _ => new ConcurrentDictionary<string, byte>());
        set[key] = 0;
    }

    /// <summary>Removes a key from the inverted index, dropping the bucket when empty.</summary>
    private void IndexRemove(string key)
    {
        var bucket = NamespaceBucketOf(key);
        if (_nsIndex.TryGetValue(bucket, out var set))
        {
            set.TryRemove(key, out _);
            if (set.IsEmpty)
                _nsIndex.TryRemove(bucket, out _);
        }
    }

    /// <summary>Authoritative ring-buffer state for an array key. Guarded by <see cref="_gate"/>.</summary>
    private sealed class RingState
    {
        public Queue<JsonElement> Entries { get; } = new();
        public bool Dirty { get; set; }
    }
}
