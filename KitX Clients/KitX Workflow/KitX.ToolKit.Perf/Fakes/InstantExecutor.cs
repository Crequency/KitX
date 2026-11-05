using KitX.ToolKit.Run;
using KitX.ToolKit.Data;

namespace KitX.ToolKit.Perf.Fakes;

/// <summary>
/// A minimal <see cref="IWorkflowExecutor"/> that returns success immediately and simulates a
/// node producing output: it publishes 5 ~1KB values, which Q48 delivers to the successor as
/// the output packet instead of writing them into the shared <see cref="DataStore"/>.
/// </summary>
internal sealed class InstantExecutor : IWorkflowExecutor
{
    private static readonly string OneKbValue = new('x', 1000);

    private readonly DataStore _store;
    private readonly bool _writeOutput;

    internal InstantExecutor(DataStore store, bool writeOutput = true)
    {
        _store = store;
        _writeOutput = writeOutput;
    }

    Task<WorkflowExecutionResult> IWorkflowExecutor.ExecuteAsync(
        string workflowId,
        string filePath,
        IReadOnlyDictionary<string, string?>? overrides,
        CancellationToken ct)
    {
        System.Text.Json.JsonElement? payload = null;
        if (_writeOutput)
        {
            var obj = new System.Text.Json.Nodes.JsonObject();
            for (var i = 0; i < 5; i++)
                obj[$"out{i}"] = OneKbValue;
            payload = System.Text.Json.JsonSerializer.SerializeToElement(obj);
        }

        return Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, payload));
    }
}
