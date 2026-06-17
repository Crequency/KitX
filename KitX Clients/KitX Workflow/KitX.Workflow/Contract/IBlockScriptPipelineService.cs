using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Contract.Models;

namespace KitX.Workflow.Contract;

/// <summary>
/// Internal pipeline extension of <see cref="KitX.Core.Contract.Workflow.IBlockScriptService"/>.
///
/// Carries the BlockScript methods that operate on the parsed <c>BlockScript</c> /
/// <c>BlockScriptParseResult</c> models — these are pipeline-internal and not consumed
/// by the Dashboard, so they are split out of the public Contract interface together
/// with the model types they reference (which also live in this namespace).
/// Implemented by the same <c>BlockScriptServiceImpl</c> that implements
/// <c>IBlockScriptService</c>.
/// </summary>
public interface IBlockScriptPipelineService
{
    /// <summary>
    /// Parses a block script from source code.
    /// </summary>
    BlockScriptParseResult ParseBlockScript(string sourceCode);

    /// <summary>
    /// Parses a block script from source code asynchronously.
    /// </summary>
    Task<BlockScriptParseResult> ParseBlockScriptAsync(string sourceCode);

    /// <summary>
    /// Executes an already-parsed block script.
    /// </summary>
    Task<KitX.Core.Contract.Workflow.BlockScriptExecutionResult> ExecuteBlockScriptAsync(
        BlockScript script,
        System.Collections.Generic.Dictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compiles a parsed BlockScript and persists the compiled assembly to disk.
    /// </summary>
    /// <param name="script">The parsed BlockScript to compile.</param>
    /// <param name="workflowId">The workflow ID for assembly naming.</param>
    /// <returns>True if compilation and persistence succeeded.</returns>
    Task<bool> CompileAndPersistAsync(BlockScript script, string workflowId);
}
