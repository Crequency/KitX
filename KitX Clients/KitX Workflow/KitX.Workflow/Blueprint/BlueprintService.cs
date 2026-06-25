using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Conversion;
using Serilog;

namespace KitX.Workflow.Blueprint;

/// <summary>
/// Blueprint service — v5.1 CFG-as-truth architecture.
/// BS → CFG → BS is the canonical path; BP is a rendered view.
/// </summary>
public class BlueprintService : IBlueprintService
{
    private readonly IBlockScriptExecutor _executor;

    public BlueprintService(IBlockScriptExecutor executor)
    {
        _executor = executor;
    }

    public KitX.Core.Contract.Workflow.Blueprint CreateBlueprint()
    {
        Log.Information("Creating new Blueprint");
        return new KitX.Core.Contract.Workflow.Blueprint
        {
            Name = "Untitled",
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now
        };
    }

    /// <summary>
    /// v5.1: Import from BS text. BS → CFG → BP graph (BP is a rendered view of CFG).
    /// NOT YET IMPLEMENTED — the CFG→BP renderer (CFGGraphRenderer, G-3) is unimplemented.
    /// Throws <see cref="NotImplementedException"/> rather than silently returning an empty
    /// blueprint, so callers fail loudly instead of receiving a graph with no nodes/connections.
    /// </summary>
    public KitX.Core.Contract.Workflow.Blueprint? ImportFromBlockScript(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        throw new NotImplementedException(
            "BS→BP conversion (ImportFromBlockScript) is not implemented in v5.1. " +
            "BP is now a rendered view of CFG; the CFG→BP renderer (CFGGraphRenderer / G-3) " +
            "is the planned replacement and is not yet built.");
    }

    /// <summary>
    /// v5.1: Export BP graph to BS text. NOT YET IMPLEMENTED — BP→BS is deferred to the
    /// BP→CFG reverse path. Throws <see cref="NotImplementedException"/> rather than silently
    /// returning an empty string, so callers fail loudly.
    /// </summary>
    public string ExportToBlockScript(KitX.Core.Contract.Workflow.Blueprint blueprint)
    {
        throw new NotImplementedException(
            "BP→BS conversion (ExportToBlockScript) is not implemented in v5.1. " +
            "BP→CFG reverse conversion is the planned replacement and is not yet built.");
    }

    public async Task<BlockScriptExecutionResult> ExecuteBlueprintAsync(KitX.Core.Contract.Workflow.Blueprint blueprint)
    {
        try
        {
            Log.Information("Executing Blueprint");
            // v5.1: fallback path — create an empty BlockScript and execute
            var blockScript = new KitX.Workflow.Models.BlockScript();
            var result = await _executor.ExecuteAsync(blockScript, null, CancellationToken.None);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to execute Blueprint");
            return new BlockScriptExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public Dictionary<string, string> GetDebugNodeMapping(KitX.Core.Contract.Workflow.Blueprint blueprint)
    {
        return new Dictionary<string, string>();
    }
}
