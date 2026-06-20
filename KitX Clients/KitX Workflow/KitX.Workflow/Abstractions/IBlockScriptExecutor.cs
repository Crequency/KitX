using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Models;

namespace KitX.Workflow.Abstractions;

/// <summary>
/// Block script executor interface — executes parsed block scripts.
///
/// The Dashboard consumes this interface directly (BlueprintEditorViewModel injects it to run
/// blueprints), so despite living in the workflow library's internal Abstractions surface it is
/// effectively a cross-module service contract. Its methods operate on the parsed
/// <c>BlockScript</c> model (also internal to this library), which is why it stays here rather
/// than in the public <c>KitX.Core.Contract.Workflow</c> — moving it would require also moving
/// <c>BlockScript</c> out of the internal model layer.
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
