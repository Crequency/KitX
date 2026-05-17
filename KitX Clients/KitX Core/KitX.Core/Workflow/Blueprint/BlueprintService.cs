using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Blueprint service implementation
/// </summary>
public class BlueprintService : IBlueprintService
{
    private readonly IBlockScriptToBlueprintConverter _toBlueprintConverter;
    private readonly IBlueprintToBlockScriptConverter _toBlockScriptConverter;
    private readonly IBlockScriptExecutor _executor;

    public BlueprintService(
        IBlockScriptToBlueprintConverter toBlueprintConverter,
        IBlueprintToBlockScriptConverter toBlockScriptConverter,
        IBlockScriptExecutor executor)
    {
        _toBlueprintConverter = toBlueprintConverter;
        _toBlockScriptConverter = toBlockScriptConverter;
        _executor = executor;
    }

    public Contract.Workflow.Blueprint CreateBlueprint()
    {
        Log.Information("Creating new Blueprint");
        return new Contract.Workflow.Blueprint
        {
            Name = "Untitled",
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now
        };
    }

    public Contract.Workflow.Blueprint? ImportFromBlockScript(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        try
        {
            Log.Information("Importing Blueprint from BlockScript");
            var blueprint = _toBlueprintConverter.Convert(sourceCode, helperFunctions);
            blueprint.ModifiedAt = DateTime.Now;
            return blueprint;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import Blueprint from BlockScript");
            return null;
        }
    }

    public string ExportToBlockScript(Contract.Workflow.Blueprint blueprint)
    {
        try
        {
            Log.Information("Exporting Blueprint to BlockScript");
            return _toBlockScriptConverter.Convert(blueprint);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export Blueprint to BlockScript");
            throw;
        }
    }

    public async Task<BlockScriptExecutionResult> ExecuteBlueprintAsync(Contract.Workflow.Blueprint blueprint)
    {
        try
        {
            Log.Information("Executing Blueprint");
            var blockScript = _toBlockScriptConverter.ConvertToBlockScript(blueprint);

            // BP→CFG→CS direct path: use the pre-built CFG to avoid redundant BS→CFG conversion
            // and preserve StatementId = node.Id for debug checkpoint highlighting.
            if (_toBlockScriptConverter is BlueprintToBlockScriptConverter concrete
                && concrete.LastCFG != null
                && _executor is BlockScripting.BlockScriptExecutor bse)
            {
                Log.Debug("[BlueprintService] Using BP→CFG→CS direct path");
                var result = await bse.ExecuteFromCFGAsync(blockScript, concrete.LastCFG, null, CancellationToken.None);

                if (concrete.LastCFG.DebugContext != null)
                    result.DebugNodeMapping = new Dictionary<string, string>(concrete.LastCFG.DebugContext.StatementToNodeId);

                return result;
            }

            // Fallback: BS→CS path
            var fallbackResult = await _executor.ExecuteAsync(blockScript, null, CancellationToken.None);
            return fallbackResult;
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

    public Dictionary<string, string> GetDebugNodeMapping(Contract.Workflow.Blueprint blueprint)
    {
        try
        {
            var blockScript = _toBlockScriptConverter.ConvertToBlockScript(blueprint);
            if (_toBlockScriptConverter is BlueprintToBlockScriptConverter concrete && concrete.LastCFG?.DebugContext != null)
                return new Dictionary<string, string>(concrete.LastCFG.DebugContext.StatementToNodeId);
            return new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to build debug node mapping");
            return new Dictionary<string, string>();
        }
    }
}
