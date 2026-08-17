namespace KitX.WorkflowV6.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;

/// <summary>
/// Executes workflow IRs with constant overrides applied, through the default
/// execution backend. Implemented by <see cref="WorkflowRunner"/>.
/// </summary>
public interface IWorkflowRunner
{
    /// <summary>
    /// Executes an IR with constant overrides applied, through the default backend.
    /// </summary>
    /// <param name="ir">The workflow IR to execute (not modified in place).</param>
    /// <param name="lowering">Optional lowering-time artefacts for the backend.</param>
    /// <param name="constantOverrides">User constant/global overrides (varName → text).</param>
    /// <param name="ct">Cancellation token for the execution.</param>
    /// <param name="debugger">Optional debug controller attached to the execution.</param>
    Task<BlockScriptExecutionResult> ExecuteAsync(
        Workflow ir,
        LoweringResult? lowering,
        IReadOnlyDictionary<string, string?>? constantOverrides,
        CancellationToken ct,
        IBlueprintDebugController? debugger = null);
}
