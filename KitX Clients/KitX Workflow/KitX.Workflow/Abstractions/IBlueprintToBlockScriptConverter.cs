namespace KitX.Workflow.Abstractions;

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