using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Links blocks sequentially for natural fallthrough execution flow.
/// v5.0: blocks no longer have implicit fall-through (§7.7) — every block must end with a
/// control-flow statement. This linker only back-fills NextBlockName for blocks that predate
/// that rule (e.g. legacy scripts or CFG-synthesized blocks); it is a no-op for well-formed
/// v5.0 scripts where every block already carries an explicit terminator.
/// </summary>
internal static class BlockLinker
{
    public static void LinkBlocksSequentially(BlockScript script)
    {
        var blocks = script.AllBlocks;

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
                if (flowStmt.ControlType != FlowControlType.ScriptReturn)
                    continue;
            }

            currentBlock.NextBlockName = nextBlock.Name;
        }
    }
}
