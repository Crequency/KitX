using System.Collections.Generic;

namespace KitX.Workflow.Contract.Models;

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
    /// Variable declarations in this block
    /// </summary>
    public List<VariableDeclaration> Variables { get; set; } = [];

    /// <summary>
    /// Statements in this block (excluding Loop statements, which are separated)
    /// </summary>
    public List<BlockStatement> Statements { get; set; } = [];

    /// <summary>
    /// Line number in source where this block starts
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Name of the next block to execute when this block ends naturally
    /// (i.e., not ended by Branch/Loop/ToLoopCond)
    /// </summary>
    public string? NextBlockName { get; set; }

    /// <summary>
    /// For LoopBlock: the block name containing this loop (i.e., the parent block)
    /// </summary>
    public string? ParentBlockName { get; set; }
}