using System.Text.Json;
using KitX.ToolKit.Contracts;

namespace KitX.ToolKit.Bench;

/// <summary>
/// One triggered execution chain (a "trigger path instance"). All workflow activations
/// spawned from a single source firing share this instance, giving them a common
/// instance-scoped cancellation token and data namespace (RFC §4.5 / §6.4). Node-level
/// join state lives here so concurrent instances never interfere.
///
/// <para>In the instance model (ToolKit 实例模型定稿) this is the dataflow engine behind a
/// <see cref="Instances.ToolkitInstance"/>: the manager wraps it to add lifecycle/Initiator
/// and retain it after completion.</para>
/// </summary>
public sealed class BenchRunInstance : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private int _pendingWorkflows;
    private int _startedCount;
    private int _completedCount;
    private int _failureCount;
    private bool _reported;
    private bool _disposed;

    internal BenchRunInstance(string toolkitId, string instanceId, Initiator initiator, string? namespaceId = null)
    {
        ToolkitId = toolkitId;
        InstanceId = instanceId;
        NamespaceId = namespaceId ?? instanceId;
        Initiator = initiator;
        // Join counters: number of distinct incoming completion edges per node.
        // Roots (no incoming edges) get 0 → any single delivery activates them.
        JoinRemaining = new Dictionary<string, int>(StringComparer.Ordinal);
        Packets = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        ActivatedNodes = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>The ToolKit id this run belongs to.</summary>
    public string ToolkitId { get; }

    /// <summary>Unique id for this run, used to scope DataStore edge keys.</summary>
    public string InstanceId { get; }

    /// <summary>
    /// The instance's shared namespace id. Defaults to <see cref="InstanceId"/>; when a
    /// UIEvent-triggered chain runs within an existing instance, the manager passes the
    /// instance's id so the panel namespace stays stable across all of the instance's runs.
    /// </summary>
    public string NamespaceId { get; }

    /// <summary>The device that initiated this run (ToolKit 实例模型定稿 D5).</summary>
    public Initiator Initiator { get; }

    /// <summary>Cancellation token for every workflow in this run (Stop cancels the whole chain).</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>Raised when every workflow in the instance has finished.</summary>
    public event EventHandler<BenchRunCompletedEventArgs>? Completed;

    /// <summary>Raised when a single workflow in the instance starts (run-monitor primitive).</summary>
    public event EventHandler<BenchNodeStartedEventArgs>? NodeStarted;

    /// <summary>Raised when a single workflow in the instance finishes (run-monitor primitive).</summary>
    public event EventHandler<BenchNodeCompletedEventArgs>? NodeCompleted;

    /// <summary>Number of workflows currently in flight.</summary>
    public int ActiveRuns => Volatile.Read(ref _pendingWorkflows);

    /// <summary>Number of workflows that have finished.</summary>
    public int CompletedRuns => Volatile.Read(ref _completedCount);

    /// <summary>Number of workflows that failed.</summary>
    public int FailedRuns => Volatile.Read(ref _failureCount);

    /// <summary>Per-node join bookkeeping. Guarded by <see cref="Gate"/>.</summary>
    internal Dictionary<string, int> JoinRemaining { get; }

    /// <summary>Per-node accumulated input packets (for <c>$output</c> resolution). Guarded by <see cref="Gate"/>.</summary>
    internal Dictionary<string, JsonElement> Packets { get; }

    /// <summary>Nodes already activated (started) in this run. Guarded by <see cref="Gate"/>.</summary>
    internal HashSet<string> ActivatedNodes { get; }

    internal object Gate => _gate;

    /// <summary>Signals that a workflow was started in this instance.</summary>
    internal void TrackStarted(string workflowId)
    {
        lock (_gate)
        {
            _pendingWorkflows++;
            _startedCount++;
        }

        NodeStarted?.Invoke(this, new BenchNodeStartedEventArgs(InstanceId, workflowId));
    }

    /// <summary>Marks a workflow as failed (propagated to the run result).</summary>
    internal void MarkFailed() => Interlocked.Increment(ref _failureCount);

    /// <summary>Signals that a workflow finished; when the last one finishes, raises <see cref="Completed"/>.</summary>
    internal void TrackCompleted(string workflowId, bool succeeded, string? error)
    {
        bool fire;
        lock (_gate)
        {
            _pendingWorkflows--;
            _completedCount++;
            if (_pendingWorkflows > 0 || _reported)
            {
                fire = false;
            }
            else
            {
                _reported = true;
                fire = true;
            }
        }

        NodeCompleted?.Invoke(this, new BenchNodeCompletedEventArgs(InstanceId, workflowId, succeeded, error));

        if (fire)
            Completed?.Invoke(this, new BenchRunCompletedEventArgs(InstanceId, _failureCount == 0));
    }

    /// <summary>Cancels every workflow in this run. Safe to call after <see cref="Dispose"/>.</summary>
    public void Cancel()
    {
        if (_disposed)
            return;
        _cts.Cancel();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Dispose();
    }
}
