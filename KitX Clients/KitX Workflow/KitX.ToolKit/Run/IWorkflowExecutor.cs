using System.Text.Json;

namespace KitX.ToolKit.Run;

/// <summary>
/// Result of running a single workflow under a run. The scheduler consumes
/// <see cref="IsSuccess"/> to decide whether to fan out, and <see cref="Payload"/> to collect
/// what the body published through <c>FlowOut</c>.
/// </summary>
/// <param name="WorkflowId">The workflow that ran.</param>
/// <param name="IsSuccess">Whether the body finished without an error.</param>
/// <param name="Error">Why it failed, when it did.</param>
/// <param name="Payload">
/// The values the body published with <c>FlowOut</c>, as a JSON object — or null when it
/// published nothing (design Q48 / §5.6.9.3). This is the transport that replaced the
/// instance-scoped slice of the store the scheduler used to scan by prefix.
/// </param>
internal sealed record WorkflowExecutionResult(string WorkflowId, bool IsSuccess, string? Error, JsonElement? Payload);

/// <summary>
/// Abstraction over "run one workflow" that the Bench scheduler depends on. Kept behind
/// an interface so the dataflow scheduler is testable with a scripted fake executor,
/// while the real <see cref="WorkflowRunnerExecutor"/> bridges to the stored IR + v6 backend.
/// </summary>
internal interface IWorkflowExecutor
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
