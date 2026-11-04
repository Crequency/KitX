using KitX.ToolKit.Contract;
using KitX.ToolKit.Run;
using KitX.ToolKit.Time;

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

    /// <summary>
    /// The injected clock (ADR-0006 D1) — this type never reads the wall clock directly.
    /// </summary>
    private readonly IClock _clock;

    private DateTimeOffset? _completedAt;

    /// <summary>Runs tracked but not yet finished.</summary>
    private int _activeRuns;

    /// <summary>Worst outcome among the runs of the current activity window; null while running.</summary>
    private RunOutcome? _windowOutcome;

    /// <summary>The outcome of the last completed activity window, once the instance went idle.</summary>
    private RunOutcome? _settledOutcome;

    /// <summary>Why the last completed window failed; null when it did not fail (ADR-0005 D4).</summary>
    private Diagnostic? _settledFailure;

    /// <summary>First failure seen in the current window. Kept so the cause survives the window.</summary>
    private Diagnostic? _windowFailure;

    internal ToolkitInstance(
        string toolkitId,
        string triggerId,
        WorkflowRun run,
        Initiator initiator,
        IClock? clock = null)
    {
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _clock = clock ?? SystemClock.Instance;
        ToolkitId = toolkitId;
        TriggerId = triggerId;
        Initiator = initiator;
        StartedAt = _clock.UtcNow;
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

    /// <summary>
    /// Whether the instance currently has work in flight. This replaces the old
    /// <c>InstanceStatus</c> enum: "running" is not a state the instance stores, it is the
    /// absence of a settled outcome, so keeping a separate field could only ever disagree with it.
    /// </summary>
    public bool IsRunning
    {
        get { lock (_gate) return _settledOutcome is null; }
    }

    /// <summary>When the instance stopped, or null while it is running.</summary>
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
        get { lock (_gate) return _settledOutcome; }
    }

    /// <summary>
    /// Why the most recent activity window failed, or null when it did not fail or is still
    /// running (ADR-0005 D4). The first failing run's diagnostic wins: it is the cause, and any
    /// later failure in the same window is its consequence.
    /// </summary>
    public Diagnostic? Failure
    {
        get { lock (_gate) return _settledFailure; }
    }

    /// <summary>
    /// True when the most recent activity window succeeded. False while running, because a
    /// verdict before the instance stops would be a guess.
    /// </summary>
    public bool Succeeded
    {
        get { lock (_gate) return _settledOutcome == RunOutcome.Succeeded; }
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
            _settledOutcome = null;
            _settledFailure = null;
            _completedAt = null;
            _windowOutcome = null; // A new run opens a new window; the previous verdict is history.
            _windowFailure = null;
        }

        run.Completed += OnRunFinished;
    }

    /// <summary>
    /// Re-opens the activity window. Called by the manager before a UIEvent chain starts on an
    /// already-completed instance, so the verdict reflects the new run (C6).
    /// </summary>
    internal void EnsureRunning()
    {
        lock (_gate)
        {
            if (_settledOutcome is null)
                return;

            _settledOutcome = null;
            _settledFailure = null;
            _completedAt = null;
            _windowOutcome = null;
            _windowFailure = null;
        }
    }

    private void OnRunFinished(object? sender, Bench.BenchRunCompletedEventArgs e)
    {
        bool fire;

        lock (_gate)
        {
            _activeRuns--;

            // Aggregate the worst outcome seen in this window: a failure anywhere in it is the
            // window's verdict, and cancellation only wins when nothing failed.
            _windowOutcome = Combine(_windowOutcome, e.Outcome);

            // The first failure is the cause; anything after it in the same window is a
            // consequence, so the earliest diagnostic is the one worth keeping (ADR-0005 D4).
            _windowFailure ??= e.Failure;

            if (_activeRuns > 0)
            {
                fire = false;
            }
            else
            {
                // Last run of the window finished: the verdict is final until new work starts.
                _completedAt = _clock.UtcNow;
                _settledOutcome = _windowOutcome ?? RunOutcome.Succeeded;
                _settledFailure = _settledOutcome == RunOutcome.Failed ? _windowFailure : null;
                fire = true;
            }
        }

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
        RunOutcome? outcome;
        Diagnostic? failure;
        DateTimeOffset? completedAt;

        lock (_gate)
        {
            active = _runs.Sum(r => r.ActiveRuns);
            completed = _runs.Sum(r => r.CompletedRuns);
            failed = _runs.Sum(r => r.FailedRuns);
            outcome = _settledOutcome;
            failure = _settledFailure;
            completedAt = _completedAt;
        }

        return new(
            InstanceId, ToolkitId, TriggerId, Initiator, StartedAt, completedAt,
            active, completed, failed)
        {
            Outcome = outcome,
            Failure = failure,
        };
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
