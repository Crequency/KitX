using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Represents a recognized block with its type, name, and source position.
/// </summary>
/// <remarks>
/// v5.0 adds <see cref="BlockVarsContent"/>, <see cref="HasExplicitBlockBody"/>, and
/// <see cref="HasBlockEnd"/> to carry the <c>##BlockVars</c>/<c>##BlockBody</c>/<c>##BlockEnd</c>
/// sub-section markers (§2.1). <see cref="Content"/> holds the body statements only
/// (the BlockVars declarations are split out into <see cref="BlockVarsContent"/>).
/// </remarks>
internal class RecognizedBlock
{
    public BlockType BlockType { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int ContentStart { get; set; }
    public int ContentEnd { get; set; }

    /// <summary>
    /// The block body statements (everything that is not a ##BlockVars declaration).
    /// For v4.0 blocks (no ## sub-sections) this is the full content as before.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// v5.0: the <c>##BlockVars</c> sub-section raw text (variable declarations only),
    /// or empty when the block has no <c>##BlockVars</c>. Parsed into
    /// <see cref="KitX.Workflow.Models.BlockDefinition.BlockVars"/> by the extractor.
    /// </summary>
    public string BlockVarsContent { get; set; } = string.Empty;

    /// <summary>
    /// v5.0: whether the block used an explicit <c>##BlockBody</c> marker to separate
    /// declarations from statements (required when <c>##BlockVars</c> is present, §2.1).
    /// </summary>
    public bool HasExplicitBlockBody { get; set; }

    /// <summary>
    /// v5.0: whether the block ended with an explicit <c>##BlockEnd</c> marker (optional, §2.1).
    /// </summary>
    public bool HasBlockEnd { get; set; }

    /// <summary>
    /// v5.1: block-level comment (// line(s) immediately preceding the #Block marker).
    /// </summary>
    public string? BlockComment { get; set; }
}