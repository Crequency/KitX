namespace KitX.Workflow.Models.Results;

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

    /// <summary>
    /// User-facing diagnostics recorded during parsing (e.g. dead-code warnings, unsupported
    /// statement forms). Empty when the parse is clean. Carried into the conversion result by
    /// <c>BlockScriptToBlueprintConverter</c> so the Dashboard editor can surface them.
    /// </summary>
    public ConversionDiagnostics Diagnostics { get; set; } = new();
}