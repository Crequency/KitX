namespace KitX.Workflow.Abstractions.Models.Statements;

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
}