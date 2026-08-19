using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.Validation;
using Serilog;

namespace KitX.ToolKit.Bench;

/// <summary>
/// The Bench execution engine (RFC §5 — harness / pure dataflow). Given a validated
/// <see cref="Toolkit"/> it runs the orchestration with exactly two control primitives:
/// <list type="bullet">
///   <item><b>fan-out</b> (1→M) — a completed workflow (or a fired source) delivers its
///   data packet to every successor edge.</item>
///   <item><b>AND-join</b> (N→1) — a workflow with incoming completion edges activates
///   only once <i>all</i> predecessors have delivered their packets (join counter).</item>
/// </list>
/// The scheduler only counts data packets; it does not do condition/loop (those live inside
/// workflows). Each <see cref="StartRun"/> creates an isolated <see cref="BenchRunInstance"/>.
/// </summary>
public sealed class BenchScheduler : IDisposable
{
    private readonly Toolkit _toolkit;
    private readonly IWorkflowExecutor _executor;
    private readonly DataStore _dataStore;
    private readonly ToolkitFileStore _fileStore;
    private readonly ConfigValidator _validator;

    // Graph: predecessor workflow → successor edges (target + its binding params).
    private Dictionary<string, List<(string To, TriggerBinding Binding)>> _successors = new(StringComparer.Ordinal);

    // AND-join: target workflow → number of distinct incoming completion edges.
    private Dictionary<string, int> _predecessorCount = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, BenchRunInstance> _instances = new();
    private bool _disposed;

    public BenchScheduler(
        Toolkit toolkit,
        IWorkflowExecutor executor,
        DataStore dataStore,
        ToolkitFileStore fileStore,
        ConfigValidator? validator = null)
    {
        _toolkit = toolkit ?? throw new ArgumentNullException(nameof(toolkit));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        _validator = validator ?? new ConfigValidator();
        BuildGraph();
    }

    /// <summary>Raised when a triggered run completes.</summary>
    public event EventHandler<BenchRunCompletedEventArgs>? RunCompleted;

    /// <summary>The ToolKit this scheduler orchestrates.</summary>
    public Toolkit Toolkit => _toolkit;

    /// <summary>Validates the ToolKit config (identity, references, strict DAG).</summary>
    public ConfigValidationResult Validate() => _validator.Validate(_toolkit);

    /// <summary>Number of currently active (in-flight) run instances.</summary>
    public int ActiveInstanceCount => _instances.Count;

    /// <summary>
    /// Starts a run from a fired trigger. Returns the created <see cref="BenchRunInstance"/>,
    /// or null when the trigger id is unknown or is a scheduler-driven WorkflowCompletion edge.
    /// <paramref name="namespaceId"/> overrides the run's DataStore namespace (used by the
    /// instance manager so UIEvent-triggered chains share the owning instance's panel namespace).
    /// <paramref name="onRunCreated"/> runs before any root workflow is scheduled, so callers
    /// (the instance manager) can subscribe to the run's node events without a race.
    /// </summary>
    public BenchRunInstance? StartRun(
        string triggerId,
        object? payload = null,
        Contracts.Initiator? initiator = null,
        string? namespaceId = null,
        Action<BenchRunInstance>? onRunCreated = null)
    {
        ThrowIfDisposed();

        var trigger = _toolkit.Triggers.FirstOrDefault(t => t.Id == triggerId);
        if (trigger is null || trigger.Type == TriggerType.WorkflowCompletion)
            return null;

        var instance = CreateInstance(initiator ?? Contracts.Initiator.Unknown, namespaceId);
        onRunCreated?.Invoke(instance);
        var packet = NormalizePayload(payload);

        // Schedule every root binding while holding the instance lock, so all roots are
        // tracked before any node body (running on a background thread) can complete and
        // fire the run's Completed event. This prevents a fast/synchronous node from
        // "completing" the run before its siblings are even started.
        lock (instance.Gate)
        {
            foreach (var binding in trigger.Bindings)
                DeliverTo(instance, binding.Workflow, packet, binding.Params);
        }

        return instance;
    }

    /// <summary>Cancels every active run instance.</summary>
    public void CancelAll()
    {
        foreach (var instance in _instances.Values)
            instance.Cancel();
    }

    private BenchRunInstance CreateInstance(Contracts.Initiator initiator, string? namespaceId = null)
    {
        var runId = Guid.NewGuid().ToString("N");
        var instance = new BenchRunInstance(_toolkit.GetId(), runId, initiator, namespaceId);

        // Initialize join counters: a node activates when its predecessor deliveries reach 0.
        lock (instance.Gate)
        {
            foreach (var wf in _toolkit.Workflows)
                instance.JoinRemaining[wf.Id] = _predecessorCount.TryGetValue(wf.Id, out var c) ? c : 0;
        }

        instance.Completed += (sender, e) =>
        {
            _instances.TryRemove(e.InstanceId, out _);
            RunCompleted?.Invoke(this, e);
        };

        _instances[instance.InstanceId] = instance;
        return instance;
    }

    /// <summary>Delivers a packet to a node. Decrements the AND-join counter; when it reaches
    /// 0 the node activates (root nodes start on their first delivery). A node already
    /// activated in this run is not activated again — a workflow that is BOTH a root
    /// delivery target and a join target runs exactly once; further deliveries are dropped.</summary>
    private void DeliverTo(
        BenchRunInstance instance,
        string target,
        JsonElement packet,
        IReadOnlyDictionary<string, string?>? bindingParams)
    {
        JsonElement mergedPacket;
        bool activate;

        lock (instance.Gate)
        {
            instance.JoinRemaining[target] = instance.JoinRemaining.TryGetValue(target, out var r)
                ? r - 1
                : -1;
            instance.Packets[target] = Merge(
                instance.Packets.TryGetValue(target, out var cur) ? cur : default,
                packet);
            mergedPacket = instance.Packets[target];

            // AND-join still waiting for more predecessors: do not activate yet.
            if (instance.JoinRemaining[target] > 0)
                return;

            // Node already activated once in this run: drop this re-delivery.
            if (!instance.ActivatedNodes.Add(target))
            {
                Log.Debug("[BenchScheduler] Node {Workflow} already activated in run {RunId}; dropping packet",
                    target, instance.InstanceId);
                return;
            }

            activate = true;
        }

        if (!activate)
            return;

        var overrides = BindingResolver.Resolve(bindingParams, mergedPacket);
        StartNode(instance, target, mergedPacket, overrides);
    }

    private void StartNode(
        BenchRunInstance instance,
        string workflowId,
        JsonElement inputPacket,
        IReadOnlyDictionary<string, string?> overrides)
    {
        instance.TrackStarted(workflowId);
        Log.Information(
            "[BenchScheduler] Node started run={RunId} namespace={Namespace} workflow={Workflow} pending={Pending}",
            instance.InstanceId, instance.NamespaceId, workflowId, instance.ActiveRuns);
        // Run the node body on a background thread so its completion never happens
        // synchronously on the scheduling thread (which would let a fast node fire the
        // run's Completed event before sibling nodes are tracked).
        _ = Task.Run(() => RunNodeAsync(instance, workflowId, inputPacket, overrides));
    }

    private async Task RunNodeAsync(
        BenchRunInstance instance,
        string workflowId,
        JsonElement inputPacket,
        IReadOnlyDictionary<string, string?> overrides)
    {
        var failed = false;
        string? error = null;

        try
        {
            var file = ResolveFile(workflowId);
            if (file is null)
            {
                failed = true;
                error = $"Workflow '{workflowId}' not found in ToolKit";
                Log.Warning("[BenchScheduler] {Error}; aborting node", error);
                return;
            }

            // Inject the instance-scoped output namespace so the workflow can write its
            // produced data via the DataStore built-in plugin without knowing the instance id.
            var ns = DataStoreScope.WorkflowNamespace(_toolkit.GetId(), instance.NamespaceId, workflowId);
            var mergedOverrides = new Dictionary<string, string?>(overrides)
            {
                [DataStoreScope.OutputNamespaceConstant] = ns,
                [Instances.InitiatorConstants.DeviceId] = instance.Initiator.DeviceId,
                [Instances.InitiatorConstants.DeviceName] = instance.Initiator.DeviceName,
                [Instances.InstanceConstants.InstanceId] = instance.NamespaceId,
            };

            // Resolve the relative config file to an absolute path; the executor loads + runs it.
            var absolutePath = _fileStore.ResolveWorkflowPath(_toolkit.GetId(), file);
            var result = await _executor.ExecuteAsync(workflowId, absolutePath, mergedOverrides, instance.Token);

            var outputPacket = BuildOutputPacket(instance, workflowId, inputPacket);
            if (result.IsSuccess)
            {
                OnNodeCompleted(instance, workflowId, outputPacket);
            }
            else
            {
                failed = true;
                error = result.Error ?? "Workflow failed";
            }
        }
        catch (OperationCanceledException)
        {
            failed = true;
            error = "Cancelled";
        }
        catch (Exception ex)
        {
            failed = true;
            error = ex.Message;
            Log.Error(ex, "[BenchScheduler] Node {Id} failed unexpectedly", workflowId);
        }
        finally
        {
            if (failed)
                instance.MarkFailed();
            Log.Information(
                "[BenchScheduler] Node finished run={RunId} namespace={Namespace} workflow={Workflow} " +
                "succeeded={Succeeded} error={Error} pendingBefore={Pending}",
                instance.InstanceId, instance.NamespaceId, workflowId, !failed, error, instance.ActiveRuns);
            instance.TrackCompleted(workflowId, !failed, error);
        }
    }

    private void OnNodeCompleted(BenchRunInstance instance, string workflowId, JsonElement outputPacket)
    {
        if (!_successors.TryGetValue(workflowId, out var edges))
            return;

        foreach (var (to, binding) in edges)
            DeliverTo(instance, to, outputPacket, binding.Params);
    }

    /// <summary>Builds a node's output packet = its input packet merged with the keys it
    /// wrote to its instance-scoped DataStore namespace (the harness "completion + data").</summary>
    private JsonElement BuildOutputPacket(BenchRunInstance instance, string workflowId, JsonElement inputPacket)
    {
        var ns = DataStoreScope.WorkflowNamespace(_toolkit.GetId(), instance.NamespaceId, workflowId);
        var prefix = ns + "/";

        var obj = inputPacket.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(inputPacket.GetRawText())?.AsObject() ?? new JsonObject()
            : new JsonObject();

        foreach (var key in _dataStore.Keys())
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || _dataStore.Get(key) is not { } value)
                continue;
            var local = key[prefix.Length..];
            if (!string.IsNullOrEmpty(local))
                obj[local] = JsonNode.Parse(value.GetRawText());
        }

        return JsonSerializer.SerializeToElement(obj);
    }

    private string? ResolveFile(string workflowId)
    {
        var wf = _toolkit.Workflows.FirstOrDefault(w => w.Id == workflowId);
        if (wf is not null && !string.IsNullOrWhiteSpace(wf.File))
            return wf.File;
        // Fall back to treating the id as the relative file path (self-contained ToolKits).
        return string.IsNullOrWhiteSpace(workflowId) ? null : workflowId;
    }

    private void BuildGraph()
    {
        var succ = new Dictionary<string, List<(string, TriggerBinding)>>(StringComparer.Ordinal);
        var pred = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var trigger in _toolkit.Triggers)
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

                if (!succ.TryGetValue(from, out var list))
                    succ[from] = list = [];
                list.Add((binding.Workflow, binding));
                pred[binding.Workflow] = pred.TryGetValue(binding.Workflow, out var c) ? c + 1 : 1;
            }
        }

        _successors = succ;
        _predecessorCount = pred;
    }

    private static JsonElement NormalizePayload(object? payload) => payload switch
    {
        null => JsonSerializer.SerializeToElement<object?>(null),
        JsonElement je => je.Clone(),
        JsonDocument jd => jd.RootElement.Clone(),
        _ => JsonSerializer.SerializeToElement(payload),
    };

    private static JsonElement Merge(JsonElement first, JsonElement second)
    {
        var obj = new JsonObject();

        if (first.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in first.EnumerateObject())
                obj[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        }

        if (second.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in second.EnumerateObject())
                obj[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
        }

        return JsonSerializer.SerializeToElement(obj);
    }

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
        foreach (var instance in _instances.Values)
            instance.Dispose();
        _instances.Clear();
    }
}
