namespace KitX.Workflow.Models;

/// <summary>
/// Block type enumeration (v5.0).
/// </summary>
/// <remarks>
/// v5.0 removed <c>LoopBlock</c>: the v4.0 auto-generated LoopBlock is gone with Loop/ToLoopCond.
/// ForLoop lives in an ordinary NamedBlock; its body is a NamedBlock that re-enters via Goto.
/// See BlockScriptGrammarRule §2.2.
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
    /// Named block — jumped to by Branch/ForLoop/Switch/Goto. May carry ##BlockVars.
    /// </summary>
    NamedBlock,

    /// <summary>
    /// Public variable block — global mutable variables, cross-block read/write (§3.2).
    /// </summary>
    PubVarBlock
}
