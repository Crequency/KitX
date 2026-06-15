namespace KitX.Workflow.Contract.Models;

/// <summary>
/// Block type enumeration
/// </summary>
public enum BlockType
{
    /// <summary>
    /// Constants block - variables are globally scoped and read-only
    /// </summary>
    ConstBlock,

    /// <summary>
    /// Main block - entry point, local scope
    /// </summary>
    MainBlock,

    /// <summary>
    /// Named block - local scope, can be called by name
    /// </summary>
    NamedBlock,

    /// <summary>
    /// Public variable block - globally scoped and writable, but not exposed in UI editor
    /// </summary>
    PubVarBlock,

    /// <summary>
    /// Loop block - auto-generated block containing a Loop statement
    /// </summary>
    LoopBlock
}