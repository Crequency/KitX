using System.Collections.Generic;

using KitX.Core.Contract.Workflow;
namespace KitX.Workflow.Contract.Models;

/// <summary>
/// Parsed block script container
/// </summary>
public class BlockScript
{
    /// <summary>
    /// Global constants block (ConstBlock)
    /// </summary>
    public BlockDefinition? ConstBlock { get; set; }

    /// <summary>
    /// Public variables block (PubVarBlock) - optional
    /// </summary>
    public BlockDefinition? PubVarBlock { get; set; }

    /// <summary>
    /// Main entry block (MainBlock)
    /// </summary>
    public BlockDefinition? MainBlock { get; set; }

    /// <summary>
    /// Named blocks dictionary by name
    /// </summary>
    public Dictionary<string, BlockDefinition> NamedBlocks { get; set; } = [];

    /// <summary>
    /// All blocks in order of appearance
    /// </summary>
    public List<BlockDefinition> AllBlocks { get; set; } = [];

    /// <summary>
    /// Loop blocks dictionary by parent block name
    /// (e.g., "MainBlock" -> LoopBlock for MainBlock's Loop statement)
    /// </summary>
    public Dictionary<string, BlockDefinition> LoopBlocks { get; set; } = [];

    /// <summary>
    /// Raw source code (parsed input, may not include helper functions)
    /// </summary>
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>
    /// Full source code including merged helper functions (for execution)
    /// </summary>
    public string FullSourceCode { get; set; } = string.Empty;

    /// <summary>
    /// Debug mapping from CFG statement IDs to Blueprint node IDs.
    /// Populated during BP→BS conversion for use by the debug execution pipeline.
    /// </summary>
    public Dictionary<string, string>? DebugNodeMapping { get; set; }

    /// <summary>
    /// Helper functions to be made available in script execution context
    /// </summary>
    public List<HelperFunction> HelperFunctions { get; set; } = [];


    /// <summary>
    /// Gets a block by name (checks LoopBlocks first by block name, then NamedBlocks, then standard blocks)
    /// IMPORTANT: LoopBlocks are checked FIRST because LoopBlock names (like "LoopBody") should NOT be
    /// shadowed by user-defined NamedBlocks with the same name.
    /// </summary>
    public BlockDefinition? GetBlockByName(string name)
    {
        // First check LoopBlocks by the block's own name (not parent block name)
        // This is critical because LoopBlocks are created for "NextBlock = Loop(...)" statements
        // and their names (like "LoopBody") should take precedence over user-defined blocks
        foreach (var kvp in LoopBlocks)
        {
            if (kvp.Value.Name == name)
                return kvp.Value;
        }

        // Then check NamedBlocks (user-defined blocks)
        if (NamedBlocks.TryGetValue(name, out var namedBlock))
            return namedBlock;

        // Then check the standard blocks by name match
        if (MainBlock?.Name == name)
            return MainBlock;
        if (ConstBlock?.Name == name)
            return ConstBlock;
        if (PubVarBlock?.Name == name)
            return PubVarBlock;

        return null;
    }
}