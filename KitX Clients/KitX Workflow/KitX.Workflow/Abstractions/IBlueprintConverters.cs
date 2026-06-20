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
