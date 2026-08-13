using KitX.ToolKit.Bench;
using KitX.ToolKit.Contracts;

namespace KitX.ToolKit.Instances;

/// <summary>
/// A runtime incarnation of a mounted ToolKit, created when a Spawn trigger fires
/// (ToolKit 实例模型定稿). One instance = one independent panel view + one isolated
/// DataStore namespace (<c>{toolkitId}/{instanceId}/...</c>) + one cancellable run chain.
///
/// <para>Wraps the dataflow <see cref="BenchRunInstance"/> and adds the lifecycle the
/// manager needs: Initiator, Running→Completed transition, and retention after completion
/// until the user explicitly ends it (D6).</para>
/// </summary>
public sealed class ToolkitInstance : IDisposable
{
    private readonly BenchRunInstance _run;
    private readonly object _gate = new();
    private InstanceStatus _status;
    private DateTimeOffset? _completedAt;

    internal ToolkitInstance(string toolkitId, string triggerId, BenchRunInstance run, Initiator initiator)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        ToolkitId = toolkitId;
        TriggerId = triggerId;
        Initiator = initiator;
        StartedAt = DateTimeOffset.UtcNow;
        _status = InstanceStatus.Running;
        _run.Completed += OnRunCompleted;
    }

    /// <summary>Unique id for this instance (scopes its DataStore namespace).</summary>
    public string InstanceId => _run.InstanceId;

    /// <summary>The ToolKit this instance belongs to.</summary>
    public string ToolkitId { get; }

    /// <summary>The Spawn trigger that created this instance.</summary>
    public string TriggerId { get; }

    /// <summary>The device that initiated this instance (D5).</summary>
    public Initiator Initiator { get; }

    /// <summary>When the instance was spawned.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Current lifecycle state (Running → Completed).</summary>
    public InstanceStatus Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>When the instance transitioned to Completed, or null while running.</summary>
    public DateTimeOffset? CompletedAt
    {
        get { lock (_gate) return _completedAt; }
    }

    /// <summary>Cancellation token for every workflow in this instance.</summary>
    public CancellationToken Token => _run.Token;

    /// <summary>Raised when the instance transitions to Completed.</summary>
    public event EventHandler? Completed;

    /// <summary>Raised when the instance is ended (cancelled + destroyed).</summary>
    public event EventHandler? Cancelled;

    private void OnRunCompleted(object? sender, BenchRunCompletedEventArgs e)
    {
        lock (_gate)
        {
            _status = InstanceStatus.Completed;
            _completedAt = DateTimeOffset.UtcNow;
        }
        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds an immutable snapshot for the run monitor / remote directory.</summary>
    public InstanceSnapshot ToSnapshot() => new(
        InstanceId, ToolkitId, TriggerId, Initiator, Status, StartedAt, CompletedAt,
        _run.ActiveRuns, _run.CompletedRuns, _run.FailedRuns);

    /// <summary>Cancels every workflow in this instance.</summary>
    public void Cancel() => _run.Cancel();

    /// <summary>Raises <see cref="Cancelled"/> (called by the manager when the instance is ended).</summary>
    internal void NotifyCancelled() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public void Dispose()
    {
        _run.Completed -= OnRunCompleted;
        _run.Dispose();
    }
}
