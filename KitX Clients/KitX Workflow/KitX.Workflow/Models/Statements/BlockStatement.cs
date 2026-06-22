namespace KitX.Workflow.Models.Statements;

/// <summary>
/// Base class for statements within a block
/// </summary>
public abstract class BlockStatement
{
    /// <summary>
    /// Debug statement ID — preserved through BP⇄BS round-trip.
    /// Set by CFG→BS conversion; consumed by BS→CFG conversion.
    /// Empty means "unset; generate a new ID".
    /// </summary>
    public string StatementId { get; set; } = string.Empty;

    /// <summary>
    /// Line number in source
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Original source code for this statement
    /// </summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>
    /// Comment attached to this statement (v5.0 bidirectional comment retention, §9).
    /// Captured from Roslyn LeadingTrivia (`//` comments above the statement, or inline
    /// trailing the previous token). For pipelines, anchors to the PipelineStatement.
    /// Round-trips BS→CFG→BS and BS→BP→BS. Null = no comment.
    /// </summary>
    public string? Comment { get; set; }
}