using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Conversion;

public interface ICfgExecutor
{
    Task<BlockScriptExecutionResult> ExecuteAsync(IWorkflowSession session, CancellationToken ct);
}
