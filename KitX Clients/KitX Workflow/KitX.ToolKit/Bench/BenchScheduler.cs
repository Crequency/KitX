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

namespace KitX.ToolKit.Bench;

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
internal sealed class BenchScheduler : IDisposable
{
    private readonly Toolkit _toolkit;
    private readonly IWorkflowExecutor _executor;
    private readonly DataStore _dataStore;
    private readonly ToolkitFileStore _fileStore;
    private readonly ConfigValidator _validator;
    private readonly BoundedNodeScheduler _nodeGate;

    /// <summary>The single completion-edge graph (ADR-0002 D3). Immutable for the scheduler's lifetime.</summary>
    private readonly WorkflowGraph _graph;

    /// <summary>The compiled form of the toolkit, when one was supplied. Its graph is authoritative.</summary>
    private readonly CompiledToolkit? _compiled;

    private readonly ConcurrentDictionary<string, WorkflowRun> _runs = new();

    /// <summary>Source of flow ids (ADR-0006 D1). Never <c>Guid.NewGuid</c> at the call site.</summary>
    private readonly IIdGenerator _ids;

    private bool _disposed;

    /// <summary>
    /// Creates a scheduler over a plain config. The completion graph is derived here with the
    /// same rules <see cref="ToolkitCompiler"/> uses, so both entries agree.
    /// </summary>
    internal BenchScheduler(
        Toolkit toolkit,
        IWorkflowExecutor executor,
        DataStore dataStore,
        ToolkitFileStore fileStore,
        ConfigValidator? validator = null,
        ToolKitRunOptions? options = null,
        IIdGenerator? idGenerator = null)
        : this(toolkit, null, executor, dataStore, fileStore, validator, options, idGenerator)
    {
    }

    /// <summary>
    /// Creates a scheduler over an already-compiled toolkit. Prefer this overload: the graph
    /// and the resolved workflow files then come from the compile step, so the scheduler cannot
    /// disagree with what was validated (ADR-0002 D1).
    /// </summary>
    internal BenchScheduler(
        Toolkit toolkit,
        CompiledToolkit? compiled,
        IWorkflowExecutor executor,
        DataStore dataStore,
        ToolkitFileStore fileStore,
        ConfigValidator? validator = null,
        ToolKitRunOptions? options = null,
        IIdGenerator? idGenerator = null)
    {
        _toolkit = toolkit ?? throw new ArgumentNullException(nameof(toolkit));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        _validator = validator ?? new ConfigValidator();
        _compiled = compiled;
        _ids = idGenerator ?? GuidIdGenerator.Instance;

        _graph = compiled?.Graph ?? BuildGraph(_toolkit);
        _nodeGate = new BoundedNodeScheduler(options?.MaxConcurrentNodes ?? 0);
    }

    /// <summary>Raised when a triggered run settles.</summary>
    public event EventHandler<BenchRunCompletedEventArgs>? RunCompleted;

    /// <summary>Raised when a run truncates: these nodes will never execute (ADR-0007 D3).</summary>
    public event EventHandler<RunNodesSkippedEventArgs>? RunNodesSkipped;

    /// <summary>The ToolKit this scheduler orchestrates.</summary>
    public Toolkit Toolkit => _toolkit;

    /// <summary>The completion-edge graph this scheduler executes.</summary>
    public WorkflowGraph Graph => _graph;

    /// <summary>Validates the ToolKit config (identity, references, strict DAG).</summary>
    public ConfigValidationResult Validate() => _validator.Validate(_toolkit);

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

        var run = CreateRun(initiator ?? Contract.Initiator.Unknown, namespaceId);
        onRunCreated?.Invoke(run);

        var packet = TriggerSourceBase.NormalizePayload(payload);

        // Seed every root binding before marking the start complete, so a synchronous fast node
        // cannot settle the run before its siblings are even known.
        foreach (var binding in trigger.Bindings)
            Deliver(run, binding.Workflow, packet, binding.Params);

        run.MarkStartComplete();
        return run;
    }

    /// <summary>Cancels every active run.</summary>
    public void CancelAll()
    {
        foreach (var run in _runs.Values)
            run.Cancel();
    }

    private Trigger? FindTrigger(string triggerId)
    {
        if (_compiled is not null)
        {
            // The compiled trigger table is a closed type hierarchy, so the bindings still come
            // from the config — the table is what tells us the trigger exists and is not an edge.
            foreach (var ct in _compiled.Triggers)
                if (string.Equals(ct.Id, triggerId, StringComparison.Ordinal))
                    return _toolkit.Triggers.FirstOrDefault(t => string.Equals(t.Id, triggerId, StringComparison.Ordinal));
            return null;
        }

        return _toolkit.Triggers.FirstOrDefault(t => t.Id == triggerId);
    }

    private WorkflowRun CreateRun(Contract.Initiator initiator, string? namespaceId)
    {
        var runId = _ids.NewFlowId();
        var joinCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in _graph.Nodes)
            joinCounts[node] = _graph.PredecessorCount(node);
        // A workflow the graph never mentions is still startable as a root; give it a counter.
        foreach (var wf in _toolkit.Workflows)
            joinCounts.TryAdd(wf.Id, 0);

        var successors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var node in joinCounts.Keys)
        {
            var edges = _graph.Successors(node);
            successors[node] = edges.Count == 0
                ? []
                : edges.Select(e => e.To).ToArray();
        }

        var run = new WorkflowRun(
            _toolkit.GetId(), runId, namespaceId ?? runId, initiator, joinCounts, successors);

        run.Completed += (sender, e) =>
        {
            _ = sender;
            _runs.TryRemove(e.InstanceId, out WorkflowRun? _);
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
        IReadOnlyDictionary<string, string?>? bindingParams)
    {
        if (!run.TryDeliver(target, packet, out var activation))
            return;

        var overrides = BindingResolver.Resolve(bindingParams, activation);
        StartNode(run, target, activation, overrides);
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
                    "[BenchScheduler] Node started run={RunId} namespace={Namespace} workflow={Workflow}",
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

        try
        {
            var file = ResolveFile(workflowId);
            if (file is null)
            {
                error = $"Workflow '{workflowId}' not found in ToolKit";
                Log.Error("[BenchScheduler] {Error}; node {Workflow} of run {RunId} failed",
                    error, workflowId, run.RunId);
                return;
            }

            var mergedOverrides = BuildOverrides(run, workflowId, overrides);
            var absolutePath = _fileStore.ResolveWorkflowPath(run.ToolkitId, file);

            var result = await _executor
                .ExecuteAsync(workflowId, absolutePath, mergedOverrides, run.Token)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                succeeded = true;
                var outputPacket = BuildOutputPacket(run, workflowId, inputPacket);
                OnNodeSucceeded(run, workflowId, outputPacket);
            }
            else
            {
                error = result.Error ?? "Workflow failed";
                Log.Error("[BenchScheduler] Node {Workflow} of run {RunId} failed: {Error}",
                    workflowId, run.RunId, error);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a failure (ADR-0009 acceptance #2); it is logged low because
            // it is an expected response to the user's stop button (ADR-0007 D4).
            cancelled = true;
            error = "Cancelled";
            Log.Debug("[BenchScheduler] Node {Workflow} of run {RunId} cancelled", workflowId, run.RunId);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error(ex, "[BenchScheduler] Node {Workflow} of run {RunId} threw",
                workflowId, run.RunId);
        }
        finally
        {
            run.SettleNode(workflowId, succeeded, error, cancelled);
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
        var ns = DataStoreScope.WorkflowNamespace(run.ToolkitId, run.NamespaceId, workflowId);
        var merged = new Dictionary<string, string?>(overrides)
        {
            [DataStoreScope.OutputNamespaceConstant] = ns,
            [Instances.InitiatorConstants.DeviceId] = run.Initiator.DeviceId,
            [Instances.InitiatorConstants.DeviceName] = run.Initiator.DeviceName,
            [Instances.InstanceConstants.InstanceId] = run.NamespaceId,
        };
        return merged;
    }

    /// <summary>Fans a successful node's output out to its successors.</summary>
    private void OnNodeSucceeded(WorkflowRun run, string workflowId, JsonElement outputPacket)
    {
        var edges = _graph.Successors(workflowId);
        if (edges.Count == 0)
            return;

        foreach (var edge in edges)
            Deliver(run, edge.To, outputPacket, edge.Binding.Params);
    }

    /// <summary>
    /// Builds a node's output packet: its input packet merged with the keys it wrote into its
    /// instance-scoped data namespace (the harness "completion + data" contract).
    /// </summary>
    private JsonElement BuildOutputPacket(WorkflowRun run, string workflowId, JsonElement inputPacket)
    {
        var ns = DataStoreScope.WorkflowNamespace(run.ToolkitId, run.NamespaceId, workflowId);
        var prefix = ns + "/";

        var obj = new JsonObject();

        if (inputPacket.ValueKind == JsonValueKind.Object)
            foreach (var prop in inputPacket.EnumerateObject())
                obj[prop.Name] = WrapElement(prop.Value);

        foreach (var key in _dataStore.KeysByPrefix(prefix))
        {
            if (_dataStore.Get(key) is not { } value)
                continue;
            var local = key[prefix.Length..];
            if (!string.IsNullOrEmpty(local))
                obj[local] = WrapElement(value);
        }

        return JsonSerializer.SerializeToElement(obj);
    }

    private string? ResolveFile(string workflowId)
    {
        if (_compiled is not null && _compiled.FindWorkflow(workflowId) is { } compiledWf)
            return string.IsNullOrWhiteSpace(compiledWf.File) ? null : compiledWf.File;

        var wf = _toolkit.Workflows.FirstOrDefault(w => w.Id == workflowId);
        if (wf is not null && !string.IsNullOrWhiteSpace(wf.File))
            return wf.File;

        // Fall back to treating the id as the relative file path (self-contained ToolKits).
        return string.IsNullOrWhiteSpace(workflowId) ? null : workflowId;
    }

    /// <summary>
    /// Derives the completion graph from config with the same rules the compiler uses. Kept so
    /// a scheduler built without a <see cref="CompiledToolkit"/> still agrees with one built
    /// with it — the S1 double-run comparison pinned that equality down.
    /// </summary>
    private static WorkflowGraph BuildGraph(Toolkit toolkit)
    {
        var edges = new List<WorkflowEdge>();

        foreach (var trigger in toolkit.Triggers)
        {
            if (trigger.Type != TriggerType.WorkflowCompletion)
                continue;

            var from = trigger.Config?.From;
            if (string.IsNullOrWhiteSpace(from))
                continue;

            foreach (var binding in trigger.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Workflow))
                    continue;

                edges.Add(new WorkflowEdge(from, binding.Workflow, binding));
            }
        }

        return WorkflowGraph.FromEdges(edges, toolkit.Workflows.Select(w => w.Id));
    }

    /// <summary>Wraps a <see cref="JsonElement"/> as a node by reference, without re-parsing its text.</summary>
    private static JsonNode WrapElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => JsonObject.Create(element),
        JsonValueKind.Array => JsonArray.Create(element),
        _ => JsonValue.Create(element)!,
    };

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BenchScheduler));
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
