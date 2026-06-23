using System.Collections.Generic;

namespace KitX.Workflow.Models;

/// <summary>
/// Represents a single block definition in the script
/// </summary>
public class BlockDefinition
{
    /// <summary>
    /// Block type
    /// </summary>
    public BlockType Type { get; set; }

    /// <summary>
    /// Block name (for NamedBlock)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Variable declarations in this block. Semantics depend on <see cref="Type"/>:
    /// ConstBlock → read-only constants; PubVarBlock → global mutable variables.
    /// </summary>
    public List<VariableDeclaration> Variables { get; set; } = [];

    /// <summary>
    /// Block-local variable declarations (##BlockVars, v5.0 §3.3). Only meaningful for
    /// MainBlock / NamedBlock. Lifetime = one block activation; reset on each entry.
    /// </summary>
    public List<VariableDeclaration> BlockVars { get; set; } = [];

    /// <summary>
    /// Whether the block body was introduced by an explicit <c>##BlockBody</c> marker.
    /// Required when <see cref="BlockVars"/> is non-empty (to separate declarations from
    /// statements). Optional otherwise. See BlockScriptGrammarRule §2.1.
    /// </summary>
    public bool HasExplicitBlockBody { get; set; }

    /// <summary>
    /// Block-level comment (v5.0 §9.3). <c>// ...</c> above or immediately after
    /// the <c>#Block Name</c> marker. Round-trips BS↔BP↔BS.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Statements in this block
    /// </summary>
    public List<BlockStatement> Statements { get; set; } = [];

    /// <summary>
    /// Line number in source where this block starts
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Name of the next block to execute when this block ends via sequential fall-through.
    /// v5.0: no implicit fall-through — a block must end with a control-flow statement
    /// (Branch/ForLoop/Switch/Goto/Break). This field carries the Goto target when the
    /// block ends with <c>Goto("name")</c>. Null when the block ends with another
    /// control-flow form. See BlockScriptGrammarRule §7.7.
    /// </summary>
    public string? NextBlockName { get; set; }

}