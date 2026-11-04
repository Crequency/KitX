using KitX.ToolKit.Contract;
using KitX.ToolKit.Run;

namespace KitX.ToolKit.Instances;

/// <summary>
/// A runtime incarnation of a mounted ToolKit, created when a Spawn trigger fires
/// (ToolKit 实例模型定稿). One instance = one independent panel view + one isolated data
/// namespace (<c>{toolkitId}/{instanceId}/...</c>) + one cancellable chain of runs.
/// </summary>
/// <remarks>
/// <para><b>What this type is not.</b> It is not the executor. A <see cref="WorkflowRun"/> is a
/// single execution and dies with it; an instance is the thing the user sees in a card, which
/// outlives any one run. So the instance keeps the run's <i>result</i>, never its state
/// (ADR-0003 D2).</para>
///
/// <para><b>Why the outcome is scoped to an activity window.</b> An instance accumulates
/// several runs over its life — the Spawn chain, then any UIEvent-triggered chains. The old
/// code kept a single <c>_anyFailed</c> flag that was set and never cleared, so one early
/// failure made the card report failure for every run afterwards: scan fifty items, fail the
/// first, and the card reads "failed" forever (ADR-0003 D3). The outcome stored here therefore
/// covers <i>the runs since the instance last went idle</i>, and resets when new work starts.</para>
/// </remarks>
public sealed class ToolkitInstance : IDisposable
{
    /// <summary>The primary (Spawn) run — the instance's token source.</summary>
    private readonly WorkflowRun _run;

    private readonly HashSet<WorkflowRun> _runs = new();
    private readonly object _gate = new();
    private InstanceStatus _status;
    private DateTimeOffset? _completedAt;

    /// <summary>Runs tracked but not yet finished.</summary>
    private int _activeRuns;

    /// <summary>Worst outcome among the runs of the current activity window; null while running.</summary>
    private RunOutcome? _windowOutcome;

    /// <summary>The outcome of the last completed activity window, once the instance went idle.</summary>
    private RunOutcome? _settledOutcome;

    internal ToolkitInstance(string toolkitId, string triggerId, WorkflowRun run, Initiator initiator)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        ToolkitId = toolkitId;
        TriggerId = triggerId;
        Initiator = initiator;
        StartedAt = DateTimeOffset.UtcNow;
        TrackRun(run);
    }

    /// <summary>Unique id for this instance (scopes its data namespace).</summary>
    public string InstanceId => _run.RunId;

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
    /// The outcome of the instance's most recent activity window — i.e. of the runs that
    /// happened since it last went idle. Null while any run is still in flight: the instance
    /// has not finished deciding, and reporting a verdict early is how a stale "failed" gets
    /// pinned onto a card that is about to succeed.
    /// </summary>
    public RunOutcome? Outcome
    {
        get
        {
            lock (_gate)
                return _status == InstanceStatus.Completed ? _settledOutcome : null;
        }
    }

    /// <summary>
    /// True when the most recent activity window succeeded. Only meaningful once the instance
    /// is <see cref="InstanceStatus.Completed"/>; while Running it reports false rather than
    /// guessing a verdict.
    /// </summary>
    public bool Succeeded
    {
        get
        {
            lock (_gate)
                return _status == InstanceStatus.Completed && _settledOutcome == RunOutcome.Succeeded;
        }
    }

    /// <summary>Raised when the instance transitions to Completed (all its runs finished).</summary>
    public event EventHandler? Completed;

    /// <summary>Raised when the instance is ended (cancelled + destroyed).</summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// Registers a child run and starts tracking its completion. Any tracked run makes the
    /// instance Running (C6 aggregation); it only returns to Completed once every tracked run
    /// has finished. Starting a run also opens a fresh activity window.
    /// </summary>
    internal void TrackRun(WorkflowRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        lock (_gate)
        {
            _runs.Add(run);
            _activeRuns++;
            _status = InstanceStatus.Running;
            _completedAt = null;
            _windowOutcome = null; // A new run opens a new window; the previous verdict is history.
        }

        run.Completed += OnRunFinished;
    }

    /// <summary>
    /// Re-transitions a Completed instance back to Running. Called by the manager before a
    /// UIEvent chain starts on an already-completed instance, so the status reflects the new
    /// run (C6).
    /// </summary>
    internal void EnsureRunning()
    {
        lock (_gate)
        {
            if (_status != InstanceStatus.Completed)
                return;

            _status = InstanceStatus.Running;
            _completedAt = null;
            _windowOutcome = null;
        }
    }

    private void OnRunFinished(object? sender, Bench.BenchRunCompletedEventArgs e)
    {
        bool fire;
        RunOutcome settled;

        lock (_gate)
        {
            _activeRuns--;

            // Aggregate the worst outcome seen in this window: a failure anywhere in it is the
            // window's verdict, and cancellation only wins when nothing failed.
            _windowOutcome = Combine(_windowOutcome, e.Outcome);

            if (_activeRuns > 0)
            {
                fire = false;
                settled = _windowOutcome ?? RunOutcome.Succeeded;
            }
            else
            {
                // Last run of the window finished: the instance is now Completed and its
                // verdict is final until new work starts.
                _status = InstanceStatus.Completed;
                _completedAt = DateTimeOffset.UtcNow;
                _settledOutcome = _windowOutcome ?? RunOutcome.Succeeded;
                settled = _settledOutcome.Value;
                fire = true;
            }
        }

        _ = settled;

        if (fire)
            Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Combines two outcomes for aggregation. Failure dominates: if part of the window failed,
    /// calling the window "cancelled" would hide the defect behind a stop button.
    /// </summary>
    private static RunOutcome Combine(RunOutcome? current, RunOutcome next)
    {
        if (current is null)
            return next;

        if (current == RunOutcome.Failed || next == RunOutcome.Failed)
            return RunOutcome.Failed;

        if (current == RunOutcome.Cancelled || next == RunOutcome.Cancelled)
            return RunOutcome.Cancelled;

        return RunOutcome.Succeeded;
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

    /// <summary>Cancels every run of this instance.</summary>
    public void Cancel()
    {
        foreach (var run in SnapshotRuns())
            run.Cancel();
    }

    /// <summary>Raises <see cref="Cancelled"/> (called by the manager when the instance is ended).</summary>
    internal void NotifyCancelled() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Copies the run set so callers can iterate without holding the instance lock across a
    /// call that may raise events (a run's <c>Cancel</c> can settle it synchronously).
    /// </summary>
    private WorkflowRun[] SnapshotRuns()
    {
        lock (_gate)
            return _runs.ToArray();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var run in SnapshotRuns())
            run.Completed -= OnRunFinished;

        _run.Dispose();
    }
}
