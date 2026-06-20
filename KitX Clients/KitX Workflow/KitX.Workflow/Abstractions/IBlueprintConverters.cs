using System.Collections.Generic;

namespace KitX.Workflow.Abstractions;

/// <summary>
/// Interface for converting BlockScript to Blueprint.
/// Internal to the workflow pipeline (consumed by <c>BlueprintService</c>).
/// </summary>
public interface IBlockScriptToBlueprintConverter
{
    /// <summary>
    /// Convert BlockScript source code to Blueprint
    /// </summary>
    /// <param name="sourceCode">BlockScript source code</param>
    /// <param name="helperFunctions">Helper functions available</param>
    /// <returns>Converted Blueprint</returns>
    KitX.Core.Contract.Workflow.Blueprint Convert(string sourceCode, List<KitX.Core.Contract.Workflow.HelperFunction>? helperFunctions = null);

    /// <summary>
    /// Convert parsed BlockScript to Blueprint
    /// </summary>
    /// <param name="script">Parsed BlockScript</param>
    /// <returns>Converted Blueprint</returns>
    KitX.Core.Contract.Workflow.Blueprint Convert(KitX.Workflow.Abstractions.Models.BlockScript script);
}

/// <summary>
/// Interface for converting Blueprint to BlockScript.
/// Internal to the workflow pipeline (consumed by <c>BlueprintService</c>).
/// </summary>
public interface IBlueprintToBlockScriptConverter
{
    /// <summary>
    /// Convert Blueprint to BlockScript source code
    /// </summary>
    /// <param name="blueprint">Blueprint to convert</param>
    /// <returns>BlockScript source code</returns>
    string Convert(KitX.Core.Contract.Workflow.Blueprint blueprint);

    /// <summary>
    /// Convert Blueprint to parsed BlockScript
    /// </summary>
    /// <param name="blueprint">Blueprint to convert</param>
    /// <returns>Parsed BlockScript</returns>
    KitX.Workflow.Abstractions.Models.BlockScript ConvertToBlockScript(KitX.Core.Contract.Workflow.Blueprint blueprint);
}
