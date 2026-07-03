using KitX.WorkflowIR.Ir.Ast;

namespace KitX.WorkflowIR.Lens.BsTextLens;

// ─────────────────────────────────────────────────────────────────────────────
// Migrated from KitX.Workflow.BlockScripting.BlockLinker.
//
// The legacy linker mutated BlockDefinition.NextBlockName (a settable property)
// in place. BlockDefinition is now an immutable record, so LinkBlocksSequentially
// can no longer mutate AllBlocks in place. It now returns a NEW BlockScript with
// rewritten blocks; the caller (BlockScriptParser) reassigns the slots.
//
// The block-termination check is preserved verbatim (a block ending in an Exit
// FlowControlStatement does not fall through).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Links blocks sequentially for natural fallthrough execution flow.
/// v5.0: blocks no longer have implicit fall-through (§7.7) — every block must end with a
/// control-flow statement. This linker only back-fills NextBlockName for blocks that predate
/// that rule (e.g. legacy scripts or CFG-synthesized blocks); it is a no-op for well-formed
/// v5.0 scripts where every block already carries an explicit terminator.
/// </summary>
internal static class BlockLinker
{
    /// <summary>
    /// Returns a new <see cref="BlockScript"/> whose AllBlocks/NamedBlocks/standard-block
    /// slots carry back-filled NextBlockName values where fall-through applies. Because
    /// BlockDefinition is an immutable record, this rebuilds the affected blocks via `with`.
    /// </summary>
    public static BlockScript LinkBlocksSequentially(BlockScript script)
    {
        var blocks = script.AllBlocks;
        if (blocks.Count <= 1) return script;

        // Index of every block that needs a NextBlockName rewrite, by position in AllBlocks.
        var rewritten = new Dictionary<int, BlockDefinition>();
        for (int i = 0; i < blocks.Count - 1; i++)
        {
            var currentBlock = blocks[i];
            var nextBlock = blocks[i + 1];

            // Skip if already has explicit control flow target.
            if (!string.IsNullOrEmpty(currentBlock.NextBlockName))
                continue;

            // Don't link across block scope boundaries that have flow control.
            // A block ending with a control-flow statement should NOT fall through.
            var lastStatement = currentBlock.Statements.LastOrDefault();
            if (lastStatement is FlowControlStatement flowStmt)
            {
                // v5.0: any control-flow terminator (Branch/ForLoop/Goto/Switch/Break) blocks
                // fall-through. IterativeCounted/UnconditionalJump are the v5.0 shapes; the v4.0
                // shapes (IterativeJump/LoopBackedge) are gone.
                if (flowStmt.FunctionName == "Exit")
                    continue;
            }

            rewritten[i] = currentBlock with { NextBlockName = nextBlock.Name };
        }

        if (rewritten.Count == 0) return script;

        // Rebuild AllBlocks, applying rewrites.
        var newAllBlocks = new List<BlockDefinition>(blocks.Count);
        for (int i = 0; i < blocks.Count; i++)
            newAllBlocks.Add(rewritten.TryGetValue(i, out var rw) ? rw : blocks[i]);

        // Re-derive the standard/named slots from the rewritten list by (Type, Name).
        // Every block carries a unique Name within its Type, so this is unambiguous.
        BlockDefinition? Pick(BlockType t, string? name) =>
            name is null ? null : newAllBlocks.FirstOrDefault(b => b.Type == t && b.Name == name);

        var namedBlocks = new Dictionary<string, BlockDefinition>();
        foreach (var (name, _) in script.NamedBlocks)
        {
            var found = Pick(BlockType.NamedBlock, name);
            if (found is not null) namedBlocks[name] = found;
        }

        return script with
        {
            AllBlocks = newAllBlocks,
            ConstBlock = Pick(BlockType.ConstBlock, script.ConstBlock?.Name),
            PubVarBlock = Pick(BlockType.PubVarBlock, script.PubVarBlock?.Name),
            MainBlock = Pick(BlockType.MainBlock, script.MainBlock?.Name),
            NamedBlocks = namedBlocks,
        };
    }
}
