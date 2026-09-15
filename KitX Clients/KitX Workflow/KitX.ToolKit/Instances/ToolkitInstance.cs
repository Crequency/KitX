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
///
/// <para><b>C6 aggregation:</b> an instance's status aggregates ALL of its child runs —
/// the Spawn run plus any UIEvent-triggered chains started on it. Any run active ⇒
/// Running; all runs complete ⇒ Completed (the transition fires when the last run
/// finishes). A UIEvent chain started on a Completed instance re-transitions it back to
/// Running via <see cref="EnsureRunning"/>. <see cref="Succeeded"/> aggregates across all
/// runs: true only when every run finished without a node failure.</para>
/// </summary>
public sealed class ToolkitInstance : IDisposable
{
    private readonly BenchRunInstance _run; // primary (Spawn) run — the instance's token source.
    private readonly HashSet<BenchRunInstance> _runs = new();
    private readonly object _gate = new();
    private InstanceStatus _status;
    private DateTimeOffset? _completedAt;
    private int _activeRuns;
    private bool _anyFailed;

    internal ToolkitInstance(string toolkitId, string triggerId, BenchRunInstance run, Initiator initiator)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        ToolkitId = toolkitId;
        TriggerId = triggerId;
        Initiator = initiator;
        StartedAt = DateTimeOffset.UtcNow;
        TrackRun(run);
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

    /// <summary>Cancellation token for the primary (Spawn) run of this instance.</summary>
    public CancellationToken Token => _run.Token;

    /// <summary>
    /// True when every tracked run finished without a node failure (C6 aggregate). While
    /// any run is still active this reflects the runs completed so far; it is only
    /// meaningful once the instance is <see cref="InstanceStatus.Completed"/>.
    /// </summary>
    public bool Succeeded
    {
        get { lock (_gate) return !_anyFailed; }
    }

    /// <summary>Raised when the instance transitions to Completed (all runs finished).</summary>
    public event EventHandler? Completed;

    /// <summary>Raised when the instance is ended (cancelled + destroyed).</summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// Registers a child run with this instance and starts tracking its completion. Any
    /// tracked run makes the instance Running (C6 aggregation); the instance only returns
    /// to Completed once every tracked run has finished.
    /// </summary>
    internal void TrackRun(BenchRunInstance run)
    {
        if (run is null)
            throw new ArgumentNullException(nameof(run));

        lock (_gate)
        {
            _runs.Add(run);
            _activeRuns++;
            _status = InstanceStatus.Running;
            _completedAt = null;
        }

        run.Completed += OnRunFinished;
    }

    /// <summary>
    /// Re-transitions a Completed instance back to Running. Called by the manager before a
    /// UIEvent chain starts on an already-completed instance, so the instance's status
    /// reflects the new active run (C6).
    /// </summary>
    internal void EnsureRunning()
    {
        lock (_gate)
        {
            if (_status == InstanceStatus.Completed)
            {
                _status = InstanceStatus.Running;
                _completedAt = null;
            }
        }
    }

    private void OnRunFinished(object? sender, BenchRunCompletedEventArgs e)
    {
        bool fire;
        lock (_gate)
        {
            _activeRuns--;
            if (!e.IsSuccess)
                _anyFailed = true;

            if (_activeRuns > 0)
            {
                // Other runs still in flight — the instance stays Running.
                fire = false;
            }
            else
            {
                // Last run finished: the instance is now Completed.
                _status = InstanceStatus.Completed;
                _completedAt = DateTimeOffset.UtcNow;
                fire = true;
            }
        }

        if (fire)
            Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Builds an immutable snapshot for the run monitor / remote directory.</summary>
    public InstanceSnapshot ToSnapshot()
    {
        int active, completed, failed;
        lock (_gate)
        {
            active = _runs.Sum(r => r.ActiveRuns);
            completed = _runs.Sum(r => r.CompletedRuns);
            failed = _runs.Sum(r => r.FailedRuns);
        }

        return new(
            InstanceId, ToolkitId, TriggerId, Initiator, Status, StartedAt, CompletedAt,
            active, completed, failed);
    }

    /// <summary>Cancels every workflow in every run of this instance.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            foreach (var run in _runs)
                run.Cancel();
        }
    }

    /// <summary>Raises <see cref="Cancelled"/> (called by the manager when the instance is ended).</summary>
    internal void NotifyCancelled() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var run in _runs)
                run.Completed -= OnRunFinished;
        }

        _run.Dispose();
    }
}
