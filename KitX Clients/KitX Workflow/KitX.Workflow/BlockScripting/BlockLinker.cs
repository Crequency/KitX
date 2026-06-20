using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Links blocks sequentially for natural fallthrough execution flow
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

            // Skip if already has explicit control flow target
            if (!string.IsNullOrEmpty(currentBlock.NextBlockName))
                continue;

            // Skip LoopBlocks (they have their own flow control)
            if (currentBlock.Type == BlockType.LoopBlock)
                continue;

            // Don't link across block scope boundaries that have flow control
            // A block ending with Branch/Loop should NOT fall through
            var lastStatement = currentBlock.Statements.LastOrDefault();
            if (lastStatement is FlowControlStatement flowStmt &&
                (flowStmt.ControlType == FlowControlType.Branch ||
                 flowStmt.ControlType == FlowControlType.Loop ||
                 flowStmt.ControlType == FlowControlType.Return))
            {
                continue;
            }

            currentBlock.NextBlockName = nextBlock.Name;
        }
    }
}