namespace KitX.Workflow.Contract.Models;

/// <summary>
/// Result of parsing operation
/// </summary>
public class BlockScriptParseResult
{
    /// <summary>
    /// Whether parsing was successful
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if parsing failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Line number where error occurred
    /// </summary>
    public int ErrorLine { get; set; }

    /// <summary>
    /// The parsed script if successful
    /// </summary>
    public BlockScript? Script { get; set; }
}