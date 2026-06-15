namespace KitX.Core.Workflow.CFG;

/// <summary>
/// Duplicates Loop condition evaluation statements before each ToLoopCond
/// in loop body blocks. This is a pure CFG transformation — it doesn't depend
/// on the walk strategy or any external context.
/// </summary>
internal class CFGConditionDuplicator
{
    /// <summary>
    /// Duplicates loop condition statements before each ToLoopCond in the CFG.
    /// This is the BP→BS Phase 2 (condition duplication).
    /// </summary>
    public void Duplicate(ControlFlowGraph cfg)
    {
        // For each LoopHeader block, find its condition evaluation statements
        foreach (var block in cfg.Blocks.Where(b => b.Type == CFGBlockType.LoopHeader))
        {
            var loopStmt = block.Statements.FirstOrDefault(s =>
                s.Kind == CFGStatementKind.Loop);
            if (loopStmt == null) continue;

            // Find the condition evaluation statements (everything before the Loop statement)
            var loopIndex = block.Statements.IndexOf(loopStmt);
            if (loopIndex <= 0) continue;

            var condStmts = block.Statements.Take(loopIndex)
                .Where(s => s.Kind is CFGStatementKind.Assignment or CFGStatementKind.Get or CFGStatementKind.Expression)
                .ToList();

            if (condStmts.Count == 0) continue;

            // Find all blocks that have a ToLoopCond edge back to this LoopHeader
            var loopBodyBlocks = cfg.Blocks.Where(b =>
                b.Successors.Any(e => e.Type == CFGEdgeType.LoopbackToCondition && e.ToBlockName == block.Name));

            foreach (var bodyBlock in loopBodyBlocks)
            {
                InsertConditionDuplicates(bodyBlock, condStmts);
            }
        }
    }

    /// <summary>
    /// Inserts condition evaluation duplicates before each ToLoopCond statement
    /// in the body block.
    /// </summary>
    private static void InsertConditionDuplicates(CFGBlock bodyBlock, List<CFGStatement> condStmts)
    {
        var insertions = new List<(int index, List<CFGStatement> stmts)>();

        for (int i = 0; i < bodyBlock.Statements.Count; i++)
        {
            var stmt = bodyBlock.Statements[i];
            if (stmt.Kind == CFGStatementKind.ToLoopCond)
            {
                var dupStmts = condStmts.Select(CloneStatement).ToList();
                insertions.Add((i, dupStmts));
            }
        }

        // Apply insertions in reverse order to preserve indices
        foreach (var (index, stmts) in insertions.OrderByDescending(x => x.index))
            bodyBlock.Statements.InsertRange(index, stmts);
    }

    /// <summary>
    /// Clones a CFGStatement, marking it as a condition duplication.
    /// </summary>
    private static CFGStatement CloneStatement(CFGStatement source) => new()
    {
        StatementId = source.StatementId,  // Same ID — these are duplicates of the same logical statement
        BlockName = source.BlockName,
        Kind = source.Kind,
        OriginalExpression = source.OriginalExpression,
        SourceLine = source.SourceLine,
        PubVarTarget = source.PubVarTarget,
        FunctionName = source.FunctionName,
        FullFunctionName = source.FullFunctionName,
        Arguments = source.Arguments,
        ConditionExpression = source.ConditionExpression,
        ConditionPubVar = source.ConditionPubVar,
        TrueBlockName = source.TrueBlockName,
        FalseBlockName = source.FalseBlockName,
        ToLoopCondReturnTo = source.ToLoopCondReturnTo,
        Fingerprint = source.Fingerprint,
        IsLoopConditionDuplication = true
    };
}