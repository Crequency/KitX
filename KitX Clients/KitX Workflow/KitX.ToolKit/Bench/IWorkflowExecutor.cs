using System.Text.Json;

namespace KitX.ToolKit.Bench;

/// <summary>
/// Result of running a single workflow under the Bench. The scheduler consumes
/// <see cref="IsSuccess"/> to decide whether to fan out; it builds the outgoing data
/// packet itself from the instance-scoped DataStore, not from this record.
/// </summary>
public sealed record WorkflowExecutionResult(string WorkflowId, bool IsSuccess, string? Error, JsonElement? Payload);

/// <summary>
/// Abstraction over "run one workflow" that the Bench scheduler depends on. Kept behind
/// an interface so the dataflow scheduler is testable with a scripted fake executor,
/// while the real <see cref="BenchWorkflowRunner"/> bridges to the stored IR + v6 backend.
/// </summary>
public interface IWorkflowExecutor
{
    /// <summary>
    /// Runs a workflow from its resolved <c>.kcs</c> <paramref name="filePath"/> (absolute)
    /// with the given constant overrides and instance-scoped cancellation. The scheduler
    /// resolves the config id → file path; the executor loads + runs it.
    /// </summary>
    Task<WorkflowExecutionResult> ExecuteAsync(
        string workflowId,
        string filePath,
        IReadOnlyDictionary<string, string?>? overrides,
        CancellationToken ct);
}
