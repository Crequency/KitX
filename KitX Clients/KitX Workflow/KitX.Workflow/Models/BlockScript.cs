using System.Collections.Generic;

using KitX.Core.Contract.Workflow;
namespace KitX.Workflow.Models;

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
    /// Gets a block by name (NamedBlocks first, then the standard blocks).
    /// v5.0 removed LoopBlock, so the former LoopBlocks-first lookup is gone.
    /// </summary>
    public BlockDefinition? GetBlockByName(string name)
    {
        // First check NamedBlocks (user-defined blocks)
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