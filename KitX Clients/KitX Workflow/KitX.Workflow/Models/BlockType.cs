namespace KitX.Workflow.Models;

/// <summary>
/// Block type enumeration.
/// </summary>
/// <remarks>
/// v5.0 transition: <c>LoopBlock</c> remains during the staged migration (removed with Loop
/// in layer 3). ForLoop lives in an ordinary NamedBlock.
/// </remarks>
public enum BlockType
{
    /// <summary>
    /// Constants block — read-only constants (v5.0: must have initial value, §3.1).
    /// </summary>
    ConstBlock,

    /// <summary>
    /// Main block — entry point.
    /// </summary>
    MainBlock,

    /// <summary>
    /// Named block — jumped to by Branch/Loop/Switch/ToLoopCond/Goto. May carry ##BlockVars.
    /// </summary>
    NamedBlock,

    /// <summary>
    /// Public variable block — global mutable variables, cross-block read/write (§3.2).
    /// </summary>
    PubVarBlock,

    /// <summary>
    /// Loop block — auto-generated block containing a Loop statement (v4.0, being removed).
    /// </summary>
    LoopBlock
}
