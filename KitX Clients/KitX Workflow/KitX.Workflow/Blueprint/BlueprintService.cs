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
    /// v5.1: Import from BS text. BS → CFG → BS (CFG is the canonical form).
    /// BP graph is rendered separately by CFGGraphRenderer (G-3).
    /// </summary>
    public KitX.Core.Contract.Workflow.Blueprint? ImportFromBlockScript(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        try
        {
            Log.Information("Importing Blueprint from BlockScript");
            // v5.1: since BP is now a rendered view, import just returns a basic blueprint
            // with the BS source stored. CFG-based rendering will replace this in G-3.
            var bp = CreateBlueprint();
            bp.Name = "Imported from BlockScript";
            bp.HelperFunctions = helperFunctions ?? [];
            bp.ModifiedAt = DateTime.Now;
            return bp;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import Blueprint from BlockScript");
            return null;
        }
    }

    public string ExportToBlockScript(KitX.Core.Contract.Workflow.Blueprint blueprint)
    {
        try
        {
            Log.Information("Exporting Blueprint to BlockScript");
            // v5.1: BP is a view — no export needed.
            // BS text is generated from CFG by CFGRenderer.
            return string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export Blueprint to BlockScript");
            throw;
        }
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
