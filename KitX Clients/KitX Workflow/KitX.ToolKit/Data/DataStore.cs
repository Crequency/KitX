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
public sealed record DataStoreChangedEventArgs(string Key, JsonElement? OldValue, JsonElement? NewValue, bool Removed, bool Appended = false);

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
public sealed class DataStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _data = new();
    private readonly object _gate = new();

    // F1: authoritative ring state per array key. Guarded by _gate. _data remains the
    // lazily-materialized external view; a ring is only re-serialized on demand (Get).
    private readonly Dictionary<string, RingState> _rings = new();

    // F2: per-key waiter buckets. Guarded by _gate. _waiters is the master set used to
    // release every pending waiter on Clear; buckets let SignalWaiters touch only the
    // waiters that could possibly be satisfied by a given key write.
    private readonly HashSet<Waiter> _waiters = [];
    private readonly Dictionary<string, HashSet<Waiter>> _waiterBuckets = new();

    // F3: inverted index from namespace bucket (first two key segments) to the keys in it.
    // Nested ConcurrentDictionary keeps the hot Set/Append path lock-free for indexing.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _nsIndex = new();

    private readonly DataStoreOptions _options;

    public DataStore(DataStoreOptions? options = null)
    {
        _options = options ?? new DataStoreOptions();
    }

    /// <summary>Raised on Set/Remove/Append, outside the store lock. Subscribers marshal themselves.</summary>
    public event EventHandler<DataStoreChangedEventArgs>? Changed;

    /// <summary>Writes a key, notifying any waiters whose condition is now satisfied.</summary>
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
        SignalWaiters(key);
        Changed?.Invoke(this, new DataStoreChangedEventArgs(key, oldValue, newValue, Removed: false));
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
        SignalWaiters(key);
        Changed?.Invoke(this, new DataStoreChangedEventArgs(key, oldValue, newValue, Removed: false, Appended: true));
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

    /// <summary>
    /// Blocks until <b>all</b> of <paramref name="keys"/> are present (AND semantics),
    /// then returns a JSON object <c>{key: value}</c>. Returns an empty object on timeout
    /// or cancellation (never throws for a cancelled token).
    /// </summary>
    public JsonElement Wait(IEnumerable<string> keys, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var keyArr = NormalizeKeys(keys);
        return Block(keyArr, any: false, timeout ?? _options.DefaultWaitTimeout, cancellationToken);
    }

    /// <summary>
    /// Blocks until <b>any</b> of <paramref name="keys"/> is present (OR semantics),
    /// then returns a JSON object of the currently-present keys. Returns an empty object
    /// on timeout or cancellation (never throws for a cancelled token).
    /// </summary>
    public JsonElement WaitAny(IEnumerable<string> keys, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var keyArr = NormalizeKeys(keys);
        return Block(keyArr, any: true, timeout ?? _options.DefaultWaitTimeout, cancellationToken);
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

    /// <summary>Drops all keys and cancels all pending waiters.</summary>
    public void Clear()
    {
        _data.Clear();
        _nsIndex.Clear();
        List<Waiter> pending;
        lock (_gate)
        {
            _rings.Clear();
            _waiterBuckets.Clear();
            pending = [.. _waiters];
            _waiters.Clear();
        }
        foreach (var w in pending)
            w.Tcs.TrySetResult(EmptyObject());
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

        var bucket = NamespaceBucketOf(prefix);
        if (!_nsIndex.TryGetValue(bucket, out var set))
            return Array.Empty<string>();
        return set.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
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
            var bucket = NamespaceBucketOf(prefix);
            if (!_nsIndex.TryGetValue(bucket, out var set))
                return 0;
            toRemove = set.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
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
            }
        }
        return removed;
    }

    /// <summary>
    /// Collects and signals waiters whose condition is now satisfied by a write to
    /// <paramref name="key"/>. Only the waiters registered against that key's bucket are
    /// examined. Caller must not hold the lock.
    /// </summary>
    private void SignalWaiters(string key)
    {
        List<Waiter> toSignal;
        lock (_gate)
        {
            if (!_waiterBuckets.TryGetValue(key, out var bucket))
                return;
            toSignal = bucket.Where(w => w.IsSatisfied(this)).ToList();
            foreach (var w in toSignal)
                RemoveWaiterFromBuckets(w);
        }
        foreach (var w in toSignal)
            w.Tcs.TrySetResult(w.BuildResult(this));
    }

    /// <summary>Removes a waiter from the master set and every key bucket it is registered against. Caller must hold the lock.</summary>
    private void RemoveWaiterFromBuckets(Waiter w)
    {
        _waiters.Remove(w);
        foreach (var key in w.Keys)
        {
            if (_waiterBuckets.TryGetValue(key, out var bucket))
            {
                bucket.Remove(w);
                if (bucket.Count == 0)
                    _waiterBuckets.Remove(key);
            }
        }
    }

    private JsonElement Block(string[] keyArr, bool any, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (keyArr.Length == 0)
            return EmptyObject();

        var waiter = new Waiter(keyArr, any);

        // Fast path: already satisfied.
        lock (_gate)
        {
            if (waiter.IsSatisfied(this))
                return waiter.BuildResult(this);
            _waiters.Add(waiter);
            foreach (var key in keyArr)
            {
                if (!_waiterBuckets.TryGetValue(key, out var bucket))
                {
                    bucket = new HashSet<Waiter>();
                    _waiterBuckets[key] = bucket;
                }
                bucket.Add(waiter);
            }
        }

        // Blocking wait on the workflow's calling thread (mirrors the PluginCall
        // TaskCompletionSource.Result precedent). A single delay with the caller's token
        // covers both timeout and cancellation: when the token fires the delay becomes
        // canceled, WhenAny returns it, and we take the empty-object branch — so a
        // cancelled run degrades exactly like a timeout (no exception into the workflow).
        var completed = Task.WhenAny(waiter.Tcs.Task, Task.Delay(timeout, cancellationToken)).GetAwaiter().GetResult();
        if (completed != waiter.Tcs.Task)
        {
            lock (_gate)
                RemoveWaiterFromBuckets(waiter);
            return EmptyObject();
        }

        return waiter.Tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>Returns an empty JSON object as a <see cref="JsonElement"/>.</summary>
    private static JsonElement EmptyObject() => JsonSerializer.SerializeToElement(new JsonObject());

    private static string[] NormalizeKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var arr = keys.Distinct().ToArray();
        if (arr.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("DataStore keys must be non-empty.", nameof(keys));
        return arr;
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

    /// <summary>A pending blocking read registered by <see cref="Block"/> and signalled by <see cref="Set"/>.</summary>
    private sealed class Waiter(string[] keys, bool any)
    {
        public string[] Keys { get; } = keys;
        public bool Any { get; } = any;
        public TaskCompletionSource<JsonElement> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsSatisfied(DataStore store)
            => Any
                ? Keys.Any(store._data.ContainsKey)
                : Keys.All(store._data.ContainsKey);

        public JsonElement BuildResult(DataStore store)
        {
            var obj = new JsonObject();
            foreach (var key in Keys)
            {
                if (store._data.TryGetValue(key, out var value))
                    obj[key] = JsonNode.Parse(value.GetRawText());
            }
            return JsonSerializer.SerializeToElement(obj);
        }
    }
}
