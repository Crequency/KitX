using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Blueprint service implementation
/// </summary>
public class BlueprintService : IBlueprintService
{
    private readonly IBlockScriptParser _parser;
    private readonly IBlockScriptExecutor _executor;

    /// <summary>
    /// Creates a new BlueprintService instance
    /// </summary>
    public BlueprintService(IBlockScriptParser parser, IBlockScriptExecutor executor)
    {
        _parser = parser;
        _executor = executor;
    }

    /// <summary>
    /// Creates a new empty blueprint
    /// </summary>
    /// <returns>New blueprint</returns>
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

    /// <summary>
    /// Imports blueprint from BlockScript source code
    /// </summary>
    /// <param name="sourceCode">BlockScript source code</param>
    /// <param name="helperFunctions">Helper functions available</param>
    /// <returns>Imported blueprint, or null if conversion failed</returns>
    public Contract.Workflow.Blueprint? ImportFromBlockScript(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        try
        {
            Log.Information("Importing Blueprint from BlockScript");

            var converter = new BlockScriptToBlueprintConverter(_parser);
            var blueprint = converter.Convert(sourceCode, helperFunctions);

            blueprint.ModifiedAt = DateTime.Now;
            return blueprint;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import Blueprint from BlockScript");
            return null;
        }
    }

    /// <summary>
    /// Exports blueprint to BlockScript source code
    /// </summary>
    /// <param name="blueprint">Blueprint to export</param>
    /// <returns>BlockScript source code</returns>
    public string ExportToBlockScript(Contract.Workflow.Blueprint blueprint)
    {
        try
        {
            Log.Information("Exporting Blueprint to BlockScript");

            var converter = new BlueprintToBlockScriptConverter();
            return converter.Convert(blueprint);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export Blueprint to BlockScript");
            throw;
        }
    }

    /// <summary>
    /// Executes blueprint by converting to BlockScript and running
    /// </summary>
    /// <param name="blueprint">Blueprint to execute</param>
    /// <returns>Execution result</returns>
    public async Task<BlockScriptExecutionResult> ExecuteBlueprintAsync(Contract.Workflow.Blueprint blueprint)
    {
        try
        {
            Log.Information("Executing Blueprint");

            // Convert blueprint to BlockScript
            var converter = new BlueprintToBlockScriptConverter();
            var blockScript = converter.ConvertToBlockScript(blueprint);

            // Execute the BlockScript
            var result = await _executor.ExecuteAsync(
                blockScript,
                null,
                CancellationToken.None);

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
}
