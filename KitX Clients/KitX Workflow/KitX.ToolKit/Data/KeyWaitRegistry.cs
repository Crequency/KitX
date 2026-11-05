namespace KitX.ToolKit.Data;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Blocks a running flow until keys appear (ADR-0004 D1: "waiting belongs to flow scheduling, not
/// to the storage API").
///
/// <para><b>Why this is not <see cref="DataStore"/>'s method.</b> A store is a string→JSON map. It
/// can say what is present; "stop this flow until something is" is a statement about the flow, and
/// the store has no flows. Keeping the two apart is what lets the store's public surface shrink to
/// the five members <see cref="KitX.ToolKit.Contract.IKeyStore"/> allows — the alternative was
/// widening the contract so a workflow could block on it.</para>
///
/// <para><b>How the race is avoided.</b> A waiter registers its handler <i>before</i> it checks the
/// store, and the handler re-checks the condition against the store rather than trusting the event
/// payload. A write that lands between registration and the first check therefore cannot be missed:
/// either the check sees it, or the event does, and both paths converge on the same
/// <see cref="TaskCompletionSource{TResult}.TrySetResult"/>.</para>
/// </summary>
internal sealed class KeyWaitRegistry : IDisposable
{
    private readonly DataStore _store;
    private readonly object _gate = new();

    // Waiters bucketed by the key they care about, so a write touches only the waiters that could
    // possibly be satisfied by it. The master set is what Clear releases wholesale.
    private readonly HashSet<Pending> _pending = [];
    private readonly Dictionary<string, HashSet<Pending>> _byKey = new(StringComparer.Ordinal);

    private bool _disposed;

    public KeyWaitRegistry(DataStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _store.Changed += OnChanged;
        _store.Cleared += OnCleared;
    }

    /// <summary>
    /// Waits until <b>all</b> of <paramref name="keys"/> are present (AND semantics), then returns
    /// a JSON object of their values. Returns an empty object on timeout or cancellation — a
    /// cancelled flow degrades exactly like a timeout rather than throwing into the workflow.
    /// </summary>
    /// <remarks>
    /// This is a genuinely asynchronous wait: it yields its thread instead of holding one
    /// (ADR-0008 D1). The three outcomes stay distinguishable at the seams even though timeout and
    /// cancellation deliberately collapse into the same return value — a workflow cannot act on the
    /// difference, and "your wait timed out" vs "your flow was stopped" is a reporting concern that
    /// belongs to the run, not to this method.
    /// </remarks>
    public Task<JsonElement> WaitAsync(
        IReadOnlyList<string> keys, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitCoreAsync(keys, any: false, timeout, cancellationToken);

    /// <summary>Same as <see cref="WaitAsync"/> but satisfied by <b>any</b> one of the keys (OR semantics).</summary>
    public Task<JsonElement> WaitAnyAsync(
        IReadOnlyList<string> keys, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitCoreAsync(keys, any: true, timeout, cancellationToken);

    private async Task<JsonElement> WaitCoreAsync(
        IReadOnlyList<string> keys, bool any, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
            return EmptyObject();

        var pending = new Pending([.. keys], any);

        // Register first, check second. A write in between is caught by the handler; a write before
        // registration is caught by the check. Neither ordering loses it.
        lock (_gate)
        {
            _pending.Add(pending);
            foreach (var key in pending.Keys)
            {
                if (!_byKey.TryGetValue(key, out var bucket))
                    _byKey[key] = bucket = [];
                bucket.Add(pending);
            }
        }

        if (pending.IsSatisfied(_store))
        {
            // Already satisfied: there is nothing to wait for, so the result is built directly
            // rather than through the completion source (which no write will ever set).
            Detach(pending);
            return pending.BuildResult(_store);
        }

        // One delay covers both timeout and cancellation: when the token fires the delay is
        // cancelled, WhenAny returns it, and we take the empty-object branch. Awaiting instead of
        // blocking is the whole point — the thread is released while this flow is parked.
        var delay = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(pending.Tcs.Task, delay).ConfigureAwait(false);
        if (completed != pending.Tcs.Task)
        {
            Detach(pending);
            return EmptyObject();
        }

        return await pending.Tcs.Task.ConfigureAwait(false);
    }

    /// <summary>Removes a waiter from every bucket it registered against.</summary>
    private void Detach(Pending pending)
    {
        lock (_gate)
        {
            _pending.Remove(pending);
            foreach (var key in pending.Keys)
            {
                if (_byKey.TryGetValue(key, out var bucket))
                {
                    bucket.Remove(pending);
                    if (bucket.Count == 0)
                        _byKey.Remove(key);
                }
            }
        }
    }

    private void OnChanged(object? sender, DataStoreChangedEventArgs e)
    {
        List<Pending> satisfied;
        lock (_gate)
        {
            if (!_byKey.TryGetValue(e.Key, out var bucket))
                return;
            satisfied = bucket.Where(p => p.IsSatisfied(_store)).ToList();
            foreach (var p in satisfied)
            {
                _pending.Remove(p);
                foreach (var key in p.Keys)
                {
                    if (_byKey.TryGetValue(key, out var b))
                    {
                        b.Remove(p);
                        if (b.Count == 0)
                            _byKey.Remove(key);
                    }
                }
            }
        }

        foreach (var p in satisfied)
            p.Tcs.TrySetResult(p.BuildResult(_store));
    }

    /// <summary>
    /// A clear wipes every key, so no waiter can ever be satisfied; each is released with an empty
    /// object the same way a timeout releases it. Leaking them would leave a flow blocked for its
    /// entire timeout on data that is provably gone.
    /// </summary>
    private void OnCleared(object? sender, EventArgs e)
    {
        List<Pending> all;
        lock (_gate)
        {
            all = [.. _pending];
            _pending.Clear();
            _byKey.Clear();
        }

        foreach (var p in all)
            p.Tcs.TrySetResult(EmptyObject());
    }

    private static JsonElement EmptyObject() => JsonSerializer.SerializeToElement(new JsonObject());

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _store.Changed -= OnChanged;
        _store.Cleared -= OnCleared;

        List<Pending> all;
        lock (_gate)
        {
            all = [.. _pending];
            _pending.Clear();
            _byKey.Clear();
        }

        foreach (var p in all)
            p.Tcs.TrySetResult(EmptyObject());
    }

    /// <summary>One blocked flow's registration.</summary>
    private sealed class Pending(string[] keys, bool any)
    {
        public string[] Keys { get; } = keys;
        public bool Any { get; } = any;

        public TaskCompletionSource<JsonElement> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsSatisfied(DataStore store)
            => Any ? Keys.Any(store.Contains) : Keys.All(store.Contains);

        public JsonElement BuildResult(DataStore store)
        {
            var obj = new JsonObject();
            foreach (var key in Keys)
            {
                if (store.TryGet(key, out var value))
                    obj[key] = JsonNode.Parse(value.GetRawText());
            }

            return JsonSerializer.SerializeToElement(obj);
        }
    }
}
