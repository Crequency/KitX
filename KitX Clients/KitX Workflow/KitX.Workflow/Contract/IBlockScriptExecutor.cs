using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Contract.Models;

namespace KitX.Workflow.Contract;

/// <summary>
/// Block script executor interface - executes parsed block scripts.
///
/// Moved to the workflow library's internal Contract surface: its methods operate on
/// the parsed <c>BlockScript</c> model (also internal), and the Dashboard no longer
/// invokes executor methods directly (it routes execution through IBlockScriptService /
/// IBlueprintService, which take source strings).
/// </summary>
public interface IBlockScriptExecutor
{
    /// <summary>
    /// Executes a block script
    /// </summary>
    /// <param name="script">The parsed block script</param>
    /// <param name="parameters">Input parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution result</returns>
    Task<BlockScriptExecutionResult> ExecuteAsync(
        BlockScript script,
        Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a block script
    /// </summary>
    BlockScriptValidationResult Validate(BlockScript script);

    /// <summary>
    /// Sets an optional debug controller for interactive execution (breakpoints, step, slow).
    /// Pass null to disable debug mode.
    /// </summary>
    void SetDebugger(IBlueprintDebugController? debugger);
}
