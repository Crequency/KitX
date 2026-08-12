using System.Text.Json;

namespace KitX.ToolKit.Bench;

/// <summary>
/// One triggered execution chain (a "trigger path instance"). All workflow activations
/// spawned from a single source firing share this instance, giving them a common
/// instance-scoped cancellation token and data namespace (RFC §4.5 / §6.4). Node-level
/// join state lives here so concurrent instances never interfere.
/// </summary>
public sealed class BenchRunInstance : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private int _pendingWorkflows;
    private int _failureCount;
    private bool _reported;

    internal BenchRunInstance(string toolkitId, string instanceId)
    {
        ToolkitId = toolkitId;
        InstanceId = instanceId;
        // Join counters: number of distinct incoming completion edges per node.
        // Roots (no incoming edges) get 0 → any single delivery activates them.
        JoinRemaining = new Dictionary<string, int>(StringComparer.Ordinal);
        Packets = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }

    /// <summary>The ToolKit id this run belongs to.</summary>
    public string ToolkitId { get; }

    /// <summary>Unique id for this run, used to scope DataStore edge keys.</summary>
    public string InstanceId { get; }

    /// <summary>Cancellation token for every workflow in this run (Stop cancels the whole chain).</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>Raised when every workflow in the instance has finished.</summary>
    public event EventHandler<BenchRunCompletedEventArgs>? Completed;

    /// <summary>Per-node join bookkeeping. Guarded by <see cref="Gate"/>.</summary>
    internal Dictionary<string, int> JoinRemaining { get; }

    /// <summary>Per-node accumulated input packets (for <c>$output</c> resolution). Guarded by <see cref="Gate"/>.</summary>
    internal Dictionary<string, JsonElement> Packets { get; }

    internal object Gate => _gate;

    /// <summary>Signals that a workflow was started in this instance.</summary>
    internal void TrackStarted()
    {
        lock (_gate)
            _pendingWorkflows++;
    }

    /// <summary>Marks a workflow as failed (propagated to the run result).</summary>
    internal void MarkFailed() => Interlocked.Increment(ref _failureCount);

    /// <summary>Signals that a workflow finished; when the last one finishes, raises <see cref="Completed"/>.</summary>
    internal void TrackCompleted()
    {
        bool fire;
        lock (_gate)
        {
            _pendingWorkflows--;
            if (_pendingWorkflows > 0 || _reported)
                return;
            _reported = true;
            fire = true;
        }

        if (fire)
            Completed?.Invoke(this, new BenchRunCompletedEventArgs(InstanceId, _failureCount == 0));
    }

    /// <summary>Cancels every workflow in this run.</summary>
    public void Cancel() => _cts.Cancel();

    /// <inheritdoc/>
    public void Dispose() => _cts.Dispose();
}
