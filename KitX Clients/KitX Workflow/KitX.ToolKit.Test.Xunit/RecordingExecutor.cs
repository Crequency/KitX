using System.Collections.Concurrent;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Data;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// A scripted <see cref="IWorkflowExecutor"/> for scheduler tests. Records each call in
/// order; can optionally write a produced value into the workflow's instance-scoped
/// DataStore namespace (so the harness "completion + data packet" is observable), can
/// fail specific workflows, and can introduce an artificial delay.
/// </summary>
public sealed class RecordingExecutor : IWorkflowExecutor
{
    private readonly DataStore _dataStore;
    private readonly bool _writeOutput;
    private readonly TimeSpan? _delay;
    private readonly HashSet<string> _failWorkflows;
    private readonly TaskCompletionSource? _hold;

    public RecordingExecutor(
        DataStore dataStore,
        bool writeOutput = true,
        TimeSpan? delay = null,
        IEnumerable<string>? failWorkflows = null,
        TaskCompletionSource? hold = null)
    {
        _dataStore = dataStore;
        _writeOutput = writeOutput;
        _delay = delay;
        _failWorkflows = new HashSet<string>(failWorkflows ?? [], StringComparer.Ordinal);
        _hold = hold;
    }

    /// <summary>Calls recorded in the order the scheduler started them.</summary>
    public ConcurrentQueue<RecordedCall> Calls { get; } = new();

    /// <summary>Workflow ids that the executor failed (injected failure), in call order.</summary>
    public List<string> Failed { get; } = [];

    /// <summary>Blocks every execution until the source is set (used to sequence a run).</summary>
    public TaskCompletionSource? Hold => _hold;

    public async Task<WorkflowExecutionResult> ExecuteAsync(
        string workflowId,
        string irData,
        IReadOnlyDictionary<string, string?>? overrides,
        CancellationToken ct)
    {
        Calls.Enqueue(new RecordedCall(workflowId, overrides is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(overrides)));

        if (_hold is not null)
            await _hold.Task.WaitAsync(ct);

        if (_delay is { } d)
            await Task.Delay(d, ct);

        if (_failWorkflows.Contains(workflowId))
        {
            Failed.Add(workflowId);
            return new WorkflowExecutionResult(workflowId, false, "injected failure", null);
        }

        // Simulate the workflow producing data into its instance-scoped namespace.
        if (_writeOutput &&
            overrides is not null &&
            overrides.TryGetValue(DataStoreScope.OutputNamespaceConstant, out var ns) &&
            !string.IsNullOrEmpty(ns))
        {
            _dataStore.Set(DataStoreScope.ScopedKey(ns, "result"), $"done:{workflowId}");
        }

        return new WorkflowExecutionResult(workflowId, true, null, null);
    }
}

/// <summary>A recorded scheduler call to the executor.</summary>
public sealed record RecordedCall(string WorkflowId, IReadOnlyDictionary<string, string?> Overrides);
