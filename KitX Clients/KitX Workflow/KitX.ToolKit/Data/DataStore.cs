using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KitX.ToolKit.Data;

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
/// </summary>
public sealed class DataStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _data = new();
    private readonly object _gate = new();
    private readonly List<Waiter> _waiters = [];
    private readonly DataStoreOptions _options;

    public DataStore(DataStoreOptions? options = null)
    {
        _options = options ?? new DataStoreOptions();
    }

    /// <summary>Writes a key, notifying any waiters whose condition is now satisfied.</summary>
    /// <param name="value">Any value — normalized to a <see cref="JsonElement"/> (JSON object / array / string / number).</param>
    public void Set(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        _data[key] = Normalize(value);

        List<Waiter> toSignal;
        lock (_gate)
        {
            toSignal = _waiters.Where(w => w.IsSatisfied(this)).ToList();
            foreach (var w in toSignal)
                _waiters.Remove(w);
        }
        foreach (var w in toSignal)
            w.Tcs.TrySetResult(w.BuildResult(this));
    }

    /// <summary>Synchronous read; null when the key is absent.</summary>
    public JsonElement? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _data.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Blocks until <b>all</b> of <paramref name="keys"/> are present (AND semantics),
    /// then returns a JSON object <c>{key: value}</c>. Returns an empty object on timeout.
    /// </summary>
    public JsonElement Wait(IEnumerable<string> keys, TimeSpan? timeout = null)
    {
        var keyArr = NormalizeKeys(keys);
        return Block(keyArr, any: false, timeout ?? _options.DefaultWaitTimeout);
    }

    /// <summary>
    /// Blocks until <b>any</b> of <paramref name="keys"/> is present (OR semantics),
    /// then returns a JSON object of the currently-present keys. Returns an empty object on timeout.
    /// </summary>
    public JsonElement WaitAny(IEnumerable<string> keys, TimeSpan? timeout = null)
    {
        var keyArr = NormalizeKeys(keys);
        return Block(keyArr, any: true, timeout ?? _options.DefaultWaitTimeout);
    }

    /// <summary>Removes a key. Returns true when it existed.</summary>
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _data.TryRemove(key, out _);
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
        List<Waiter> pending;
        lock (_gate)
        {
            pending = [.. _waiters];
            _waiters.Clear();
        }
        foreach (var w in pending)
            w.Tcs.TrySetResult(EmptyObject());
    }

    private JsonElement Block(string[] keyArr, bool any, TimeSpan timeout)
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
        }

        // Blocking wait on the workflow's calling thread (mirrors the PluginCall
        // TaskCompletionSource.Result precedent). Timeout unblocks a never-satisfied wait.
        var completed = Task.WhenAny(waiter.Tcs.Task, Task.Delay(timeout)).GetAwaiter().GetResult();
        if (completed != waiter.Tcs.Task)
        {
            lock (_gate)
                _waiters.Remove(waiter);
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
