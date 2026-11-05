using System.Text.Json;
using System.Text.Json.Nodes;
using KitX.ToolKit.Run;
using KitX.ToolKit.Contract;

namespace KitX.ToolKit.Run;

/// <summary>
/// One execution of a ToolKit's workflow graph — every piece of mutable state for a single
/// run lives here and nowhere else (ADR-0003 D1).
/// </summary>
/// <remarks>
/// <para><b>Why this type exists.</b> Before the split, "what is this run doing right now?"
/// had no single answer. It was spread across <c>JoinRemaining</c>, <c>Packets</c>,
/// <c>ActivatedNodes</c>, <c>_pendingWorkflows</c> and <c>_failureCount</c> on the run object,
/// plus <c>Status</c> and <c>_anyFailed</c> on the instance. The scheduler maintained those
/// fields' invariants from outside, under a lock it borrowed from the run
/// (<c>instance.Gate</c>) — so the rules lived in one class and the state in another. Here
/// they share a class, and the caller can only <i>ask</i>.</para>
///
/// <para><b>The lock does not leave this class.</b> No <c>internal object Gate</c>, no
/// <c>lock (run.Something)</c> from outside. Each transition decides and mutates atomically,
/// then reports what happened.</para>
///
/// <para><b>Layering (ADR-0003 D2).</b> This type must not reference the <c>Instances</c>
/// namespace. A run is one execution; an instance is the persistent thing that outlives it.
/// Anything instance-shaped — including the reserved constant names a workflow sees — is
/// injected by the scheduler, which is allowed to know both.</para>
///
/// <para><b>The outcome is produced here, not guessed above (ADR-0003 D3).</b> The run decides
/// whether it succeeded, failed or was cancelled, and exposes only the settled answer. The
/// instance layer stores that answer; it does not accumulate its own failure flag that can
/// never be reset.</para>
///
/// <para><b>A run covers the subgraph a trigger can reach, not the whole graph.</b> Nodes the
/// trigger never feeds stay <see cref="RunNodeState.Pending"/> forever by design — waiting on
/// an AND-join whose other predecessor belongs to a different trigger path. The run therefore
/// settles when nothing is in flight and nothing is queued, not when every node is terminal.</para>
///
/// <para><b>Threading.</b> Events are raised <i>outside</i> the lock so a subscriber can call
/// back into the run without deadlocking. The cost is that two events may interleave on
/// different threads; subscribers must tolerate that, and the counters they read are snapshots
/// taken under the lock.</para>
/// </remarks>
internal sealed class WorkflowRun : IDisposable
{
    /// <summary>Node id → remaining join deliveries. Roots start at 0.</summary>
    private readonly Dictionary<string, int> _joinRemaining;

    /// <summary>Node id → accumulated input packet, merged across predecessor deliveries.</summary>
    private readonly Dictionary<string, JsonElement> _packets;

    /// <summary>Node id → current state. Covers every node in the graph, reached or not.</summary>
    private readonly Dictionary<string, RunNodeState> _states;

    /// <summary>Successor table, used only to propagate truncation.</summary>
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _successors;

    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private int _activated;
    private int _running;
    private int _completed;
    private int _failed;
    private int _cancelled;
    private int _skipped;
    private bool _started;
    private bool _settled;
    private bool _disposed;

    /// <summary>
    /// The first failure that decided this run's verdict (ADR-0005 D4). Held so the reason can
    /// be reported with the outcome instead of being reconstructed afterwards from logs.
    /// </summary>
    private Diagnostic? _firstFailure;

    internal WorkflowRun(
        string toolkitId,
        string runId,
        string namespaceId,
        Initiator initiator,
        IReadOnlyDictionary<string, int> joinCounts,
        IReadOnlyDictionary<string, IReadOnlyList<string>> successors)
    {
        ToolkitId = toolkitId ?? throw new ArgumentNullException(nameof(toolkitId));
        RunId = runId ?? throw new ArgumentNullException(nameof(runId));
        NamespaceId = namespaceId ?? runId;
        Initiator = initiator ?? throw new ArgumentNullException(nameof(initiator));
        _successors = successors ?? throw new ArgumentNullException(nameof(successors));

        _joinRemaining = new Dictionary<string, int>(joinCounts, StringComparer.Ordinal);
        _packets = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        _states = new Dictionary<string, RunNodeState>(joinCounts.Count, StringComparer.Ordinal);
        foreach (var node in joinCounts.Keys)
            _states[node] = RunNodeState.Pending;
    }

    /// <summary>The ToolKit this run belongs to.</summary>
    public string ToolkitId { get; }

    /// <summary>Unique id for this run.</summary>
    public string RunId { get; }

    /// <summary>
    /// The data namespace this run writes under. Equals <see cref="RunId"/> for a
    /// trigger-initiated run; for a UIEvent chain started inside an existing instance it is
    /// that instance's id, so the panel namespace stays stable across the instance's runs.
    /// </summary>
    public string NamespaceId { get; }

    /// <summary>The device that initiated this run.</summary>
    public Initiator Initiator { get; }

    /// <summary>Cancels every node of this run.</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>Raised when a node's body actually starts, i.e. after it obtained a slot.</summary>
    public event EventHandler<NodeStartedEventArgs>? NodeStarted;

    /// <summary>
    /// Raised when a node settled after running. Skipped nodes do not raise this — they never
    /// ran, and reporting them here would claim a body executed (see <see cref="NodesSkipped"/>).
    /// </summary>
    public event EventHandler<NodeCompletedEventArgs>? NodeCompleted;

    /// <summary>
    /// Raised once per truncation wave with the nodes that will never run because an upstream
    /// node failed (ADR-0007 D3). Without it, a failed node's downstream is observationally
    /// identical to a node that was never in the graph.
    /// </summary>
    public event EventHandler<RunNodesSkippedEventArgs>? NodesSkipped;

    /// <summary>Raised exactly once, when the run has nothing left in flight or queued.</summary>
    public event EventHandler<RunCompletedEventArgs>? Completed;

    /// <summary>Number of node bodies currently in flight.</summary>
    public int ActiveRuns
    {
        get { lock (_gate) return _running; }
    }

    /// <summary>Number of nodes that settled after running (completed + failed + cancelled).</summary>
    public int CompletedRuns
    {
        get { lock (_gate) return _completed; }
    }

    /// <summary>Number of nodes that ran and failed.</summary>
    public int FailedRuns
    {
        get { lock (_gate) return _failed; }
    }

    /// <summary>
    /// The settled outcome, or <see langword="null"/> while the run is in flight. Once
    /// non-null it never changes — the run is done deciding.
    /// </summary>
    public RunOutcome? Outcome
    {
        get
        {
            lock (_gate)
                return _settled ? DecideOutcome() : null;
        }
    }

    /// <summary>
    /// Why the run failed, or <see langword="null"/> when it did not fail or has not settled.
    /// </summary>
    /// <remarks>
    /// Exposed alongside <see cref="Outcome"/> rather than folded into it, so a caller can read
    /// the verdict without paying for a diagnostic it will not display. The reason is decided at
    /// the same moment as the verdict and never mutated afterwards (ADR-0005 D4).
    /// </remarks>
    public Diagnostic? Failure
    {
        get
        {
            lock (_gate)
                return _settled ? DecideFailure() : null;
        }
    }

    /// <summary>A point-in-time copy of every node's state. Safe to hold after the call returns.</summary>
    public IReadOnlyDictionary<string, RunNodeState> NodeStates
    {
        get
        {
            lock (_gate)
                return new Dictionary<string, RunNodeState>(_states, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Delivers a predecessor's packet to <paramref name="target"/> and reports whether that
    /// delivery completed the target's AND-join.
    /// </summary>
    /// <remarks>
    /// A successful return <b>claims</b> the node (it moves to <see cref="RunNodeState.Activated"/>),
    /// so a second concurrent delivery cannot also claim it. The caller must then either call
    /// <see cref="BeginNode"/> or release the claim via <see cref="AbandonNode"/>; until one of
    /// those happens the node is neither running nor available, which is exactly the window in
    /// which it is queued for an execution slot.
    /// </remarks>
    /// <param name="target">Workflow id receiving the packet.</param>
    /// <param name="packet">The predecessor's output packet.</param>
    /// <param name="activation">On success, the packet the node should start with.</param>
    /// <param name="conflict">
    /// On failure, the property name both branches published. Non-null means this delivery could
    /// not be merged (design Q69) and the target node has been marked failed.
    /// </param>
    /// <returns><see langword="true"/> when this delivery claimed the node.</returns>
    internal bool TryDeliver(
        string target,
        JsonElement packet,
        out JsonElement activation,
        out string? conflict)
    {
        conflict = null;

        lock (_gate)
        {
            activation = default;

            if (!_states.TryGetValue(target, out var state) || state != RunNodeState.Pending)
                return false; // Unknown node, or already claimed/settled: the delivery is dropped.

            var remaining = (_joinRemaining.TryGetValue(target, out var r) ? r : 0) - 1;
            _joinRemaining[target] = remaining;

            var previous = _packets.TryGetValue(target, out var current) ? current : default;

            // Assembling a join means two parallel branches now contribute to one packet. A name
            // both of them published is a modelling error (design Q69): picking a winner would make
            // the node's inputs depend on which branch finished first.
            if (!TryMergeJoin(previous, packet, out var merged, out var collided))
            {
                conflict = collided;
                _packets.Remove(target);
                _states[target] = RunNodeState.Failed;
                _failed++;

                _firstFailure ??= Diagnostic.Error(
                    Diagnostics.RunDiagnosticCodes.JoinConflict,
                    $"Workflow '{target}' would receive '{collided}' from two branches; " +
                    "an AND-join cannot choose one without depending on arrival order",
                    new DiagnosticPath(WorkflowId: target, Key: collided));

                return false;
            }

            _packets[target] = merged;

            if (remaining > 0)
                return false; // AND-join still waiting on other predecessors.

            _states[target] = RunNodeState.Activated;
            _activated++;
            activation = merged;
            return true;
        }
    }

    /// <summary>
    /// Marks a claimed node as actually executing. Called after the caller obtained an
    /// execution slot, so <see cref="ActiveRuns"/> counts bodies genuinely in flight rather
    /// than bodies queued behind the concurrency limit.
    /// </summary>
    internal void BeginNode(string workflowId)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(workflowId, out var state) || state != RunNodeState.Activated)
                return;

            _states[workflowId] = RunNodeState.Running;
            _activated--;
            _running++;
        }

        NodeStarted?.Invoke(this, new NodeStartedEventArgs(RunId, workflowId));
    }

    /// <summary>
    /// Releases a claim without running the node — no slot was ever obtained, or the run was
    /// cancelled while it was queued.
    /// </summary>
    internal void AbandonNode(string workflowId)
    {
        bool fire;
        lock (_gate)
        {
            if (_states.TryGetValue(workflowId, out var state) && state == RunNodeState.Activated)
            {
                _states[workflowId] = RunNodeState.Cancelled;
                _activated--;
                _cancelled++;
                _completed++;
            }

            fire = TrySettle();
        }

        if (fire)
            Completed?.Invoke(this, BuildCompletedArgs());
    }

    /// <summary>
    /// Marks the caller done seeding the run. Settles it if nothing was activated at all (an
    /// empty trigger, or a binding naming a workflow that is not in the graph), which would
    /// otherwise leave a run that never fires <see cref="Completed"/>.
    /// </summary>
    internal void MarkStartComplete()
    {
        bool fire;
        lock (_gate)
        {
            _started = true;
            fire = TrySettle();
        }

        if (fire)
            Completed?.Invoke(this, BuildCompletedArgs());
    }

    /// <summary>
    /// Settles one node that ran. Raises node completion, any resulting truncation wave, and —
    /// when the run is left with nothing outstanding — <see cref="Completed"/>.
    /// </summary>
    /// <param name="workflowId">The node that finished.</param>
    /// <param name="succeeded">Whether the body completed without error.</param>
    /// <param name="error">Failure or cancellation detail.</param>
    /// <param name="cancelled">
    /// True when the node was cancelled rather than failed. The two are different outcomes
    /// (ADR-0009 acceptance #2) and must not be merged into one bucket.
    /// </param>
    internal void SettleNode(string workflowId, bool succeeded, string? error, bool cancelled = false)
    {
        IReadOnlyList<string>? skipped = null;
        bool fire;

        lock (_gate)
        {
            if (!_states.TryGetValue(workflowId, out var state) || state != RunNodeState.Running)
                return;

            _running--;
            _completed++;

            if (succeeded)
            {
                _states[workflowId] = RunNodeState.Completed;
            }
            else if (cancelled)
            {
                _states[workflowId] = RunNodeState.Cancelled;
                _cancelled++;
            }
            else
            {
                _states[workflowId] = RunNodeState.Failed;
                _failed++;

                // The first failure is the cause; later ones are its consequences (ADR-0005 D4).
                _firstFailure ??= Diagnostic.Error(
                    Diagnostics.RunDiagnosticCodes.WorkflowFailed,
                    error ?? $"Workflow '{workflowId}' failed",
                    new DiagnosticPath(WorkflowId: workflowId));

                // A failed node can never deliver, so every node downstream whose AND-join
                // depends on it will never be satisfied. Mark them now rather than leaving
                // them Pending forever (ADR-0007 D3).
                skipped = MarkDownstreamSkipped(workflowId);
            }

            fire = TrySettle();
        }

        NodeCompleted?.Invoke(this, new NodeCompletedEventArgs(RunId, workflowId, succeeded, error));

        if (skipped is { Count: > 0 })
            NodesSkipped?.Invoke(this, new RunNodesSkippedEventArgs(RunId, skipped, workflowId));

        if (fire)
            Completed?.Invoke(this, BuildCompletedArgs());
    }

    /// <summary>
    /// Cancels the run. Nodes already running observe <see cref="Token"/> and settle as
    /// cancelled; nodes queued for a slot are moved to cancelled here so the run can reach a
    /// terminal state without waiting on a slot that will never be granted.
    /// </summary>
    public void Cancel()
    {
        if (_disposed)
            return;

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        bool fire;
        lock (_gate)
        {
            foreach (var node in _states.Keys.ToArray())
            {
                if (_states[node] != RunNodeState.Activated)
                    continue;

                _states[node] = RunNodeState.Cancelled;
                _activated--;
                _cancelled++;
                _completed++;
            }

            fire = TrySettle();
        }

        if (fire)
            Completed?.Invoke(this, BuildCompletedArgs());
    }

    /// <summary>
    /// Breadth-first walk over a failed node's successors, marking every still-Pending node as
    /// skipped and continuing through it. Caller holds the lock.
    /// </summary>
    /// <remarks>
    /// <para>Nodes already claimed or running are left alone: they were activated before the
    /// failure and will settle on their own. The walk does not continue through them, since
    /// their own successors may still be reachable.</para>
    ///
    /// <para>Each node is skipped at most once (the state test guards it) and the visited set
    /// guards diamonds, so the walk terminates on any graph shape.</para>
    /// </remarks>
    private IReadOnlyList<string> MarkDownstreamSkipped(string failedNode)
    {
        var skipped = new List<string>();
        if (!_successors.TryGetValue(failedNode, out var direct))
            return skipped;

        var queue = new Queue<string>(direct);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node))
                continue;

            if (!_states.TryGetValue(node, out var state) || state != RunNodeState.Pending)
                continue;

            _states[node] = RunNodeState.Skipped;
            _skipped++;
            skipped.Add(node);

            if (_successors.TryGetValue(node, out var next))
                foreach (var n in next)
                    queue.Enqueue(n);
        }

        return skipped;
    }

    /// <summary>
    /// Settles the run when nothing is in flight and nothing is queued, returning whether it
    /// just transitioned so the caller raises <see cref="Completed"/> exactly once. Caller
    /// holds the lock.
    /// </summary>
    /// <remarks>
    /// <see cref="RunNodeState.Pending"/> nodes do not block settling. A node whose AND-join
    /// another trigger path was supposed to satisfy is legitimately unreachable from this run,
    /// and waiting for it would leave the run hanging forever.
    /// </remarks>
    private bool TrySettle()
    {
        if (_settled || !_started)
            return false;

        if (_running > 0 || _activated > 0)
            return false;

        _settled = true;
        return true;
    }

    /// <summary>The run's verdict. Caller holds the lock and the run is settled.</summary>
    /// <remarks>
    /// A failure outranks a cancellation: if any node genuinely failed, reporting the run as
    /// merely cancelled would hide a defect behind the user's stop button. Cancellation becomes
    /// the verdict only when nothing failed.
    /// </remarks>
    private RunOutcome DecideOutcome()
    {
        if (_failed > 0)
            return RunOutcome.Failed;
        if (_cancelled > 0)
            return RunOutcome.Cancelled;
        return RunOutcome.Succeeded;
    }

    /// <summary>
    /// The diagnostic explaining a <see cref="RunOutcome.Failed"/> verdict, or null otherwise.
    /// Caller holds the lock.
    /// </summary>
    /// <remarks>
    /// The <i>first</i> failure is reported, not the last. A run truncates on the first failure,
    /// so the first one is the cause and everything after it is consequence; reporting the last
    /// would surface whichever node happened to lose a race.
    /// </remarks>
    private Diagnostic? DecideFailure()
    {
        if (_failed == 0)
            return null;

        return _firstFailure ?? Diagnostic.Error(
            Diagnostics.RunDiagnosticCodes.Unknown,
            "The run failed but no node recorded a reason");
    }

    /// <summary>Builds the completion payload. Caller holds the lock.</summary>
    private RunCompletedEventArgs BuildCompletedArgs() =>
        new(RunId, DecideOutcome(), _failed, _completed, _skipped, DecideFailure());

    /// <summary>Merges two object packets; <paramref name="second"/> wins on duplicate names.</summary>
    private static JsonElement Merge(JsonElement first, JsonElement second)
    {
        var obj = new JsonObject();

        if (first.ValueKind == JsonValueKind.Object)
            foreach (var prop in first.EnumerateObject())
                obj[prop.Name] = Wrap(prop.Value);

        if (second.ValueKind == JsonValueKind.Object)
            foreach (var prop in second.EnumerateObject())
                obj[prop.Name] = Wrap(prop.Value);

        return JsonSerializer.SerializeToElement(obj);
    }

    /// <summary>
    /// Merges two object packets, refusing to pick a winner when both carry the same name
    /// (design Q69 §5.6.7).
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists next to <see cref="Merge"/>.</b> The two do different jobs. This
    /// one decides whether an AND-join can activate at all; <see cref="Merge"/> is still the right
    /// answer for a single-predecessor delivery, where there is nothing to collide with.</para>
    ///
    /// <para><b>Why the collision is an error rather than "last one wins".</b> A join combines the
    /// output of two branches that ran in parallel. When both name a property the same thing,
    /// which value the node sees would be decided by <i>which branch happened to finish first</i>
    /// — a race that changes results without changing any input. The design's ruling is that this
    /// is a modelling error by the author, and the run reports it instead of hiding it.</para>
    /// </remarks>
    /// <param name="first">The packet delivered so far, or <see langword="default"/> if none.</param>
    /// <param name="second">The packet arriving now.</param>
    /// <param name="conflict">On failure, the duplicated property name.</param>
    /// <returns><see langword="false"/> when the two packets collide.</returns>
    private static bool TryMergeJoin(JsonElement first, JsonElement second, out JsonElement merged, out string? conflict)
    {
        merged = default;
        conflict = null;

        var firstIsObject = first.ValueKind == JsonValueKind.Object;
        var secondIsObject = second.ValueKind == JsonValueKind.Object;

        if (!firstIsObject || !secondIsObject)
        {
            // Nothing can collide. `default` means "this is the first delivery" and must not win
            // over the packet that just arrived — picking it would silently discard the only data
            // the node was going to get.
            if (firstIsObject)
                merged = secondIsObject ? Merge(first, second) : first;
            else if (secondIsObject)
                merged = second;
            else
                merged = Merge(first, second); // Neither is an object: same empty-packet result.

            return true;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in first.EnumerateObject())
            names.Add(prop.Name);

        foreach (var prop in second.EnumerateObject())
        {
            if (names.Contains(prop.Name))
            {
                conflict = prop.Name;
                return false;
            }
        }

        merged = Merge(first, second);
        return true;
    }

    /// <summary>
    /// Wraps a <see cref="JsonElement"/> as a node by reference, without re-parsing its raw
    /// text. Objects and arrays use their reference factories; primitives use
    /// <see cref="JsonValue.Create(JsonElement)"/>.
    /// </summary>
    /// <remarks>
    /// The two <c>Create</c> overloads are declared nullable because they return null when the
    /// element is not of the expected kind. The switch has already established the kind, so the
    /// suppressions state a fact rather than hiding a hazard.
    /// </remarks>
    private static JsonNode Wrap(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => JsonObject.Create(element)!,
        JsonValueKind.Array => JsonArray.Create(element)!,
        _ => JsonValue.Create(element)!,
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_gate)
            _settled = true;

        _cts.Dispose();
    }
}
