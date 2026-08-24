using KitX.ToolKit.Bench;
using KitX.ToolKit.Data;

namespace KitX.ToolKit.Perf.Fakes;

/// <summary>
/// A minimal <see cref="IWorkflowExecutor"/> that returns success immediately and simulates
/// the <c>BenchOut</c> builtin's real workload: reads the injected output namespace and
/// writes 5 ~1KB values into the shared <see cref="DataStore"/> under that namespace.
/// </summary>
public sealed class InstantExecutor : IWorkflowExecutor
{
    private static readonly string OneKbValue =
        "{\"payload\":\"" + new string('x', 1000) + "\"}";

    private readonly DataStore _store;
    private readonly bool _writeOutput;

    public InstantExecutor(DataStore store, bool writeOutput = true)
    {
        _store = store;
        _writeOutput = writeOutput;
    }

    public Task<WorkflowExecutionResult> ExecuteAsync(
        string workflowId,
        string filePath,
        IReadOnlyDictionary<string, string?>? overrides,
        CancellationToken ct)
    {
        if (_writeOutput &&
            overrides is not null &&
            overrides.TryGetValue(DataStoreScope.OutputNamespaceConstant, out var ns) &&
            !string.IsNullOrEmpty(ns))
        {
            for (var i = 0; i < 5; i++)
                _store.Set(DataStoreScope.ScopedKey(ns, $"out{i}"), OneKbValue);
        }

        return Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
