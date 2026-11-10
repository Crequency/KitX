using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using KitX.ToolKit.Compile;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;
using KitX.ToolKit.Run;
using KitX.ToolKit.Time;
using KitX.ToolKit.Triggers;
using Serilog;

namespace KitX.ToolKit.Run;

/// <summary>
/// The Bench execution engine — a pure dataflow harness. Given a validated
/// <see cref="Toolkit"/> it runs the orchestration with exactly two control primitives:
/// <list type="bullet">
///   <item><b>fan-out</b> (1→M) — a completed workflow (or a fired source) delivers its data
///   packet to every successor edge.</item>
///   <item><b>AND-join</b> (N→1) — a workflow with incoming completion edges activates only
///   once <i>all</i> predecessors have delivered their packets.</item>
/// </list>
///
/// <para><b>Where the state lives.</b> The scheduler holds only what is fixed for the life of
/// the ToolKit: the compiled graph, the executor, the store. Everything that varies per
/// execution lives in <see cref="WorkflowRun"/>, which the scheduler asks questions of rather
/// than reaching into (ADR-0003 D1). There is no lock here that is shared with a run.</para>
///
/// <para><b>Concurrency.</b> Node bodies run through a bounded gate
/// (<see cref="BoundedNodeScheduler"/>, ADR-0008 D2). Run admission is a separate limit
/// described on <see cref="ToolKitRunOptions"/>; the scheduler does not conflate them.</para>
/// </summary>
internal sealed class RunScheduler : IDisposable
{
    private readonly IWorkflowExecutor _executor;
    private readonly string _toolkitRoot;
    private readonly BoundedNodeScheduler _nodeGate;

    /// <summary>The single completion-edge graph (ADR-0002 D3). Immutable for the scheduler's lifetime.</summary>
    private readonly WorkflowGraph _graph;

    /// <summary>The compiled form of the toolkit. Its graph is authoritative.</summary>
    private readonly CompiledToolkit _compiled;

    private readonly ConcurrentDictionary<string, WorkflowRun> _runs = new();

    /// <summary>
    /// Per target node, the union of the packet property names its incoming edges declared
    /// (design Q70). Computed once — the graph is immutable — so the filter applied at delivery
    /// is a property of the node's position, not of which predecessor happened to arrive last.
    /// </summary>
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _consumesByTarget;

    /// <summary>
    /// The set handed to a trigger-path delivery: no packet property becomes a constant.
    /// </summary>
    /// <remarks>
    /// A named constant rather than an inline empty set, because the value is a decision and the
    /// name is where the decision is explained. See the call site in <c>StartRun</c>.
    /// </remarks>
    private static readonly IReadOnlySet<string> EmptyConsumes =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Source of flow ids (ADR-0006 D1). Never <c>Guid.NewGuid</c> at the call site.</summary>
    private readonly IIdGenerator _ids;

    private bool _disposed;

    /// <summary>
    /// Creates a scheduler over an already-compiled toolkit. Everything the scheduler needs — the
    /// graph, the trigger bindings, the workflow file table, the toolkit id — comes from the
    /// compile step, so the scheduler cannot disagree with what was validated and never has to
    /// consult the config again (ADR-0002 D1).
    /// </summary>
    /// <remarks>
    /// <para>There is deliberately no overload that derives the graph from the config. One
    /// existed while the compiled form was being introduced, and it never stopped being the one
    /// production used — the compiler was exercised only by its own tests. Now the only way to
    /// build a scheduler is to compile first.</para>
    ///
    /// <para>The scheduler used to take the <c>Toolkit</c> as well. That made "the runtime must
    /// never take a Toolkit again" false while still passing every grep the criterion named: the
    /// graph came from the compiled form, but triggers, workflow files and the toolkit id were
    /// re-read from the config beside it. Reading one fact from two places is exactly the failure
    /// ADR-0002 exists to remove, so the parameter is gone rather than merely unused.</para>
    /// </remarks>
    /// <param name="toolkitRoot">
    /// The root directory this toolkit's workflow files live under — <c>Data/Toolkits</c>, with one
    /// sub-directory per toolkit id.
    ///
    /// <para><b>Why a root and not a file store.</b> The parameter used to be a
    /// <c>ToolkitFileStore</c>, and that type accepted any directory: eight test sites passed
    /// <c>new ToolkitFileStore(Path.GetTempPath())</c>, compiled cleanly, and meant "some directory"
    /// rather than "this toolkit's". The store also brought <see cref="IToolkitWorkflowFileStore"/>'s
    /// load and save along, neither of which the scheduler does — it resolves one path per node and
    /// stops.</para>
    ///
    /// <para>A string is not self-describing either, so it is named for the relationship and the
    /// resolution is <see cref="Storage.ToolkitFileStore.Resolve"/>, which is the one place the path
    /// rule and its escape check live.</para>
    /// </param>
    internal RunScheduler(
        CompiledToolkit compiled,
        IWorkflowExecutor executor,
        string toolkitRoot,
        ToolKitRunOptions? options = null,
        IIdGenerator? idGenerator = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _toolkitRoot = toolkitRoot ?? throw new ArgumentNullException(nameof(toolkitRoot));
        _compiled = compiled ?? throw new ArgumentNullException(nameof(compiled));
        _ids = idGenerator ?? GuidIdGenerator.Instance;

        _graph = _compiled.Graph;
        _nodeGate = new BoundedNodeScheduler(options?.MaxConcurrentNodes ?? 0);

        // Union the declared names per target once. See the field's remarks for why per-node and
        // not per-edge: a join activates with one merged packet, so filtering by the edge that
        // happened to arrive last would make the visible names depend on a race.
        _consumesByTarget = _graph.Edges
            .GroupBy(e => e.To, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<string>)new HashSet<string>(
                    g.SelectMany(e => e.Consumes), StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    /// <summary>Raised when a triggered run settles.</summary>
    public event EventHandler<RunCompletedEventArgs>? RunCompleted;

    /// <summary>Raised when a run truncates: these nodes will never execute (ADR-0007 D3).</summary>
    public event EventHandler<RunNodesSkippedEventArgs>? RunNodesSkipped;

    /// <summary>The completion-edge graph this scheduler executes.</summary>
    public WorkflowGraph Graph => _graph;

    /// <summary>Number of currently active runs.</summary>
    public int ActiveInstanceCount => _runs.Count;

    /// <summary>Maximum node bodies a single run may execute at once.</summary>
    public int MaxConcurrentNodes => _nodeGate.MaxConcurrency;

    /// <summary>
    /// Starts a run from a fired trigger, or returns <see langword="null"/> when the trigger is
    /// unknown or is a scheduler-driven <see cref="TriggerType.WorkflowCompletion"/> edge.
    /// </summary>
    /// <param name="triggerId">The trigger that fired.</param>
    /// <param name="payload">Trigger payload, normalised into the root packet.</param>
    /// <param name="initiator">Who asked for this run.</param>
    /// <param name="namespaceId">Overrides the run's data namespace so a UIEvent chain started
    /// inside an existing instance shares that instance's panel namespace.</param>
    /// <param name="onRunCreated">Invoked before any root workflow is scheduled, so callers can
    /// subscribe without racing the first node.</param>
    public WorkflowRun? StartRun(
        string triggerId,
        object? payload = null,
        Contract.Initiator? initiator = null,
        string? namespaceId = null,
        Action<WorkflowRun>? onRunCreated = null)
    {
        ThrowIfDisposed();

        var trigger = FindTrigger(triggerId);
        if (trigger is null || trigger.Type == TriggerType.WorkflowCompletion)
            return null;

        var run = CreateRun(trigger.Id, initiator ?? Contract.Initiator.Unknown, namespaceId);
        onRunCreated?.Invoke(run);

        var packet = TriggerSourceBase.NormalizePayload(payload);

        // Seed every root binding before marking the start complete, so a synchronous fast node
        // cannot settle the run before its siblings are even known.
        //
        // The empty set is deliberate, not a placeholder (design Q89/Q91). Passing the binding's
        // own names here would look like forwarding and do nothing: BindingResolver.Resolve has
        // already written every one of those names into the overrides, so every TryAdd below
        // would fail. What the empty set actually says is "this packet contributes no free
        // constants" — a payload property only becomes a constant when a `$payload.x` reference
        // names it.
        foreach (var binding in trigger.Bindings)
            Deliver(run, binding.Workflow, packet, binding.Params, EmptyConsumes);

        run.MarkStartComplete();
        return run;
    }

    /// <summary>Cancels every active run.</summary>
    public void CancelAll()
    {
        foreach (var run in _runs.Values)
            run.Cancel();
    }

    private CompiledTrigger? FindTrigger(string triggerId)
    {
        foreach (var ct in _compiled.Triggers)
            if (string.Equals(ct.Id, triggerId, StringComparison.Ordinal))
                return ct;
        return null;
    }

    private WorkflowRun CreateRun(string triggerId, Contract.Initiator initiator, string? namespaceId)
    {
        var runId = _ids.NewFlowId();
        var joinCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in _graph.Nodes)
            joinCounts[node] = _graph.PredecessorCount(node);
        // A workflow the graph never mentions is still startable as a root; give it a counter.
        foreach (var workflowId in _compiled.Workflows.Keys)
            joinCounts.TryAdd(workflowId, 0);

        var successors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var node in joinCounts.Keys)
        {
            var edges = _graph.Successors(node);
            successors[node] = edges.Count == 0
                ? []
                : edges.Select(e => e.To).ToArray();
        }

        var run = new WorkflowRun(
            _compiled.ToolkitId, runId, namespaceId ?? runId, triggerId, initiator, joinCounts, successors);

        run.Completed += (sender, e) =>
        {
            _ = sender;

            // The two names below are the same value: RunCompletedEventArgs' first argument is
            // RunId (see WorkflowRun.BuildCompletedArgs). Storing under RunId and removing under
            // FlowId only works because of that. If either side ever changes, this run silently
            // stays in the table, ActiveInstanceCount never drops, and a slot of the machine-wide
            // MaxConcurrentFlows budget is gone for the life of the process.
            //
            // RunOutcomeTests asserts ActiveInstanceCount returns to zero after a run settles,
            // which is what would catch it.
            _runs.TryRemove(e.FlowId, out WorkflowRun? _);
            RunCompleted?.Invoke(this, e);
        };
        run.NodesSkipped += (sender, e) =>
        {
            _ = sender;
            RunNodesSkipped?.Invoke(this, e);
        };

        _runs[run.RunId] = run;
        return run;
    }

    /// <summary>
    /// Delivers a packet to a node and, when that completed its join, schedules it. The
    /// activation decision belongs to the run; the scheduler only acts on the answer.
    /// </summary>
    private void Deliver(
        WorkflowRun run,
        string target,
        JsonElement packet,
        IReadOnlyDictionary<string, string?>? bindingParams,
        IReadOnlySet<string>? consumes = null)
    {
        if (!run.TryDeliver(target, packet, out var activation, out var conflict))
        {
            // A join conflict is a failure of this run, not a dropped packet: the run already
            // recorded the diagnostic and marked the node failed, so all that is left is to give
            // the run a chance to settle (it may now have nothing left in flight).
            if (conflict is not null)
                Log.Warning(
                    "[RunScheduler] AND-join conflict run={RunId} workflow={Workflow} property={Property}",
                    run.RunId, target, conflict);

            return;
        }

        // The node's constant overrides come from two places, and only one of them is undeclared:
        // the trigger's binding params (declared, resolved against the trigger payload) and the
        // packet properties the edge says this successor consumes (declared by name only, design
        // Q70 — the values still come from the packet).
        //
        // Packet properties must be visible to the node body at all, otherwise removing edge
        // values would have removed the data channel with them: FlowOut publishes a value,
        // BuildOutputPacket collects it into the packet, and the only way a downstream FlowIn can
        // see it again is for the packet's own properties to be readable as names.
        //
        // `consumes == null` used to mean "the trigger path, where there is no upstream author to
        // collide with". That reason expired: since Q90/Q91 the trigger payload comes from a View
        // client, so there IS an upstream author and it may be anyone. The trigger path now passes
        // an empty set like any edge that forwards nothing, and a payload property only becomes a
        // constant when a `$payload.x` reference names it (BindingResolver, above).
        //
        // Null is kept as a distinguishable state rather than folded into the empty set, because
        // the two say different things: "nobody said" versus "somebody said none".
        var overrides = new Dictionary<string, string?>(BindingResolver.Resolve(bindingParams, activation));

        if (consumes is null || consumes.Count > 0)
            foreach (var (name, value) in PacketConstants(activation))
                if (consumes is null || consumes.Contains(name))
                    overrides.TryAdd(name, value);

        StartNode(run, target, activation, overrides);
    }

    /// <summary>
    /// Flattens the delivered packet into constant-override entries, so a downstream workflow can
    /// read what its predecessor published without an edge having to declare anything
    /// (design Q44/Q49: "the successor receives the predecessor's output packet").
    /// </summary>
    /// <remarks>
    /// Only top-level scalar properties become entries. Nested objects and arrays are skipped
    /// deliberately: a constant override is a string, so exposing a subtree would mean inventing
    /// a serialisation rule at the boundary and a naming rule for its children. A workflow that
    /// needs a nested value publishes that value under its own name with <c>FlowOut</c>.
    ///
    /// <para>Declared binding params win over packet entries of the same name: the binding is the
    /// author's explicit instruction, the packet is ambient. <c>Deliver</c> applies the resolved
    /// bindings first for exactly that reason.</para>
    /// </remarks>
    private static IEnumerable<KeyValuePair<string, string?>> PacketConstants(JsonElement packet)
    {
        if (packet.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in packet.EnumerateObject())
        {
            var rendered = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => null, // object / array: not a scalar, not a constant
            };

            if (rendered is not null)
                yield return new KeyValuePair<string, string?>(property.Name, rendered);
        }
    }

    /// <summary>
    /// Runs a node body once a concurrency slot is available. The node is already claimed by
    /// the run, so nothing else can start it; if the run is cancelled while it waits, the claim
    /// is released as cancelled rather than counting as a failure.
    /// </summary>
    private void StartNode(
        WorkflowRun run,
        string workflowId,
        JsonElement inputPacket,
        IReadOnlyDictionary<string, string?> overrides)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _nodeGate.AcquireAsync(run.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                run.AbandonNode(workflowId);
                return;
            }
            catch (ObjectDisposedException)
            {
                run.AbandonNode(workflowId);
                return;
            }

            try
            {
                run.BeginNode(workflowId);
                Log.Verbose(
                    "[RunScheduler] Node started run={RunId} namespace={Namespace} workflow={Workflow}",
                    run.RunId, run.NamespaceId, workflowId);

                await RunNodeAsync(run, workflowId, inputPacket, overrides).ConfigureAwait(false);
            }
            finally
            {
                _nodeGate.Release();
            }
        });
    }

    private async Task RunNodeAsync(
        WorkflowRun run,
        string workflowId,
        JsonElement inputPacket,
        IReadOnlyDictionary<string, string?> overrides)
    {
        var succeeded = false;
        var cancelled = false;
        string? error = null;
        string? failureCode = null;

        try
        {
            var file = ResolveFile(workflowId);
            if (file is null)
            {
                error = $"Workflow '{workflowId}' not found in ToolKit";
                failureCode = "flow.workflow.not-found";
                Log.Error("[RunScheduler] {Error}; node {Workflow} of run {RunId} failed",
                    error, workflowId, run.RunId);
                return;
            }

            var mergedOverrides = BuildOverrides(run, workflowId, overrides);
            var absolutePath = ToolkitFileStore.Resolve(_toolkitRoot, run.ToolkitId, file);

            var result = await _executor
                .ExecuteAsync(workflowId, absolutePath, mergedOverrides, run.Token)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                succeeded = true;
                var outputPacket = BuildOutputPacket(inputPacket, result.Payload);
                OnNodeSucceeded(run, workflowId, outputPacket);
            }
            else
            {
                error = result.Error ?? "Workflow failed";
                failureCode = result.FailureCode;
                Log.Error("[RunScheduler] Node {Workflow} of run {RunId} failed: {Error}",
                    workflowId, run.RunId, error);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a failure (ADR-0009 acceptance #2); it is logged low because
            // it is an expected response to the user's stop button (ADR-0007 D4).
            cancelled = true;
            error = "Cancelled";
            Log.Debug("[RunScheduler] Node {Workflow} of run {RunId} cancelled", workflowId, run.RunId);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            failureCode = "flow.workflow.threw";
            Log.Error(ex, "[RunScheduler] Node {Workflow} of run {RunId} threw",
                workflowId, run.RunId);
        }
        finally
        {
            run.SettleNode(workflowId, succeeded, error, cancelled, failureCode);
        }
    }

    /// <summary>
    /// Builds the constant overrides handed to a workflow. The instance-shaped names live in
    /// the <c>Instances</c> namespace, which the run deliberately does not know about
    /// (ADR-0003 D2) — injecting them is the scheduler's job, since it is allowed to see both.
    /// </summary>
    private Dictionary<string, string?> BuildOverrides(
        WorkflowRun run,
        string workflowId,
        IReadOnlyDictionary<string, string?> overrides)
    {
        var merged = new Dictionary<string, string?>(overrides)
        {
            [Instances.InitiatorConstants.DeviceId] = run.Initiator.DeviceId,
            [Instances.InitiatorConstants.DeviceName] = run.Initiator.DeviceName,
            [Instances.InitiatorConstants.TriggerId] = run.TriggerId,
            [Instances.InstanceConstants.ToolkitId] = run.ToolkitId,
        };
        return merged;
    }

    /// <summary>Fans a successful node's output out to its successors.</summary>
    private void OnNodeSucceeded(WorkflowRun run, string workflowId, JsonElement outputPacket)
    {
        var edges = _graph.Successors(workflowId);
        if (edges.Count == 0)
            return;

        // An edge carries topology and names, never values (design Q44 + Q70): the successor
        // receives whatever names its incoming edges declared, and nothing else. Passing null for
        // the binding params is what makes $output. references impossible downstream — there is no
        // binding left to write one on — while the declared names decide which packet properties
        // may reach the next node's constants.
        foreach (var edge in edges)
            Deliver(run, edge.To, outputPacket, bindingParams: null, NamesFor(edge.To));
    }

    /// <summary>
    /// The packet property names a node is allowed to see: the union of what every incoming edge
    /// declared.
    /// </summary>
    /// <remarks>
    /// <b>The union is per node, not per delivery.</b> A join activates once, with the merged
    /// packet from all its predecessors, so filtering by the last-arriving edge's names alone
    /// would silently drop whatever the earlier edges declared — and which edge arrives last is a
    /// race, so the node's visible names would be nondeterministic. Returning the union makes the
    /// filter a property of the node's position in the graph rather than of arrival order.
    /// </remarks>
    private IReadOnlySet<string>? NamesFor(string target)
        => _consumesByTarget.TryGetValue(target, out var names) ? names : null;

    /// <summary>
    /// Builds a node's output packet: its input packet merged with the values its body published
    /// through <c>FlowOut</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Where the published values come from</b> (design Q48 / §5.6.9.3). The body hands
    /// them back through the execution result's payload. There used to be a second source: the
    /// body wrote them into an instance-scoped slice of the process-wide store and this method
    /// scanned that prefix. It existed only because <c>FlowOut</c> runs while the body is still
    /// executing — an ordering artefact, not a semantic need — and it made a node's output
    /// outlive the run that produced it.</para>
    ///
    /// <para><b>The input packet is still passed through.</b> A node that forwards without
    /// publishing anything must not silently drop what it received; that is Q48's other half, and
    /// the join in <see cref="WorkflowRun"/> relies on it for nodes with more than one predecessor.</para>
    /// </remarks>
    private static JsonElement BuildOutputPacket(JsonElement inputPacket, JsonElement? published)
    {
        var obj = new JsonObject();

        if (inputPacket.ValueKind == JsonValueKind.Object)
            foreach (var prop in inputPacket.EnumerateObject())
                obj[prop.Name] = WrapElement(prop.Value);

        if (published is { ValueKind: JsonValueKind.Object } body)
            foreach (var prop in body.EnumerateObject())
                obj[prop.Name] = WrapElement(prop.Value);

        return JsonSerializer.SerializeToElement(obj);
    }

    private string? ResolveFile(string workflowId)
    {
        // The compiled workflow table is the only source of file names. The config fallback that
        // used to live here read the same fact a second time, which is what ADR-0002 D1 forbids
        // — and it is also how the scheduler kept a live dependency on the config.
        if (_compiled.FindWorkflow(workflowId) is { } compiledWf)
            return string.IsNullOrWhiteSpace(compiledWf.File) ? null : compiledWf.File;

        // Fall back to treating the id as the relative file path (self-contained ToolKits).
        return string.IsNullOrWhiteSpace(workflowId) ? null : workflowId;
    }

    /// <summary>Wraps a <see cref="JsonElement"/> as a node by reference, without re-parsing its text.</summary>
    private static JsonNode WrapElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => JsonObject.Create(element)!,
        JsonValueKind.Array => JsonArray.Create(element)!,
        _ => JsonValue.Create(element)!,
    };

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RunScheduler));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        CancelAll();
        foreach (var run in _runs.Values)
            run.Dispose();
        _runs.Clear();
        _nodeGate.Dispose();
    }
}
