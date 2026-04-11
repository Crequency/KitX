using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;

namespace KitX.Core.Workflow.Blueprint.ReversePipeline;

/// <summary>
/// Phase 3 of reverse conversion: duplicates Loop condition evaluation statements
/// before each ToLoopCond in loop body blocks.
/// </summary>
internal class ConditionDuplicator
{
    public void Duplicate(ReverseConversionContext ctx)
    {
        foreach (var kvp in ctx.LoopBodyBlocks)
        {
            var loopNodeId = kvp.Key;
            var loopBodyBlock = kvp.Value;

            if (!ctx.LoopNodes.TryGetValue(loopNodeId, out var loopNode)) continue;

            var flow = ctx.ControlFlowMap.TryGetValue(loopNodeId, out var f) ? f : null;
            if (flow == null) continue;

            var conditionExpr = flow.ConditionExpression;
            if (string.IsNullOrEmpty(conditionExpr)) continue;

            var condStmts = FindConditionStatements(loopNode, ctx);
            if (condStmts.Count == 0) continue;

            // Insert duplicates before each ToLoopCond in the loop body block
            var insertions = new List<(int index, List<BlockStatement> stmts)>();

            for (int i = 0; i < loopBodyBlock.Statements.Count; i++)
            {
                var stmt = loopBodyBlock.Statements[i];
                if (stmt is FlowControlStatement flowStmt
                    && flowStmt.ControlType == FlowControlType.ToLoopCond)
                {
                    var dupStmts = condStmts.Select(CloneStatement).ToList();
                    insertions.Add((i, dupStmts.Cast<BlockStatement>().ToList()));
                }
            }

            // Apply insertions in reverse order to preserve indices
            foreach (var (index, stmts) in insertions.OrderByDescending(x => x.index))
                loopBodyBlock.Statements.InsertRange(index, stmts);
        }
    }

    /// <summary>
    /// Finds the condition evaluation statements for a Loop node.
    /// These are the Get/CallHelper statements that compute the condition value.
    /// </summary>
    private List<ExpressionStatement> FindConditionStatements(BlueprintNode loopNode,
        ReverseConversionContext ctx)
    {
        var result = new List<ExpressionStatement>();

        var conditionPin = loopNode.InputPins.FirstOrDefault(p => p.Name == Condition);
        if (conditionPin == null) return result;

        if (!ctx.InputDataMap.TryGetValue((loopNode.Id, Condition), out var condInfo))
            return result;

        var chainPubVars = new HashSet<string>();
        CollectConditionChain(condInfo, ctx, chainPubVars);

        if (chainPubVars.Count == 0) return result;

        if (ctx.Script.MainBlock == null) return result;

        var loopFlowStmt = ctx.ControlFlowMap.TryGetValue(loopNode.Id, out var lf) ? lf : null;
        if (loopFlowStmt == null) return result;

        var loopIndex = ctx.Script.MainBlock.Statements.IndexOf(loopFlowStmt);
        if (loopIndex < 0) return result;

        for (int i = loopIndex - 1; i >= 0; i--)
        {
            var stmt = ctx.Script.MainBlock.Statements[i];
            if (stmt is ExpressionStatement exprStmt)
            {
                foreach (var pv in chainPubVars)
                {
                    if (exprStmt.SourceCode.StartsWith($"{pv} = "))
                    {
                        result.Insert(0, exprStmt);
                        break;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Recursively collects all PubVar names in the data chain leading to a condition.
    /// </summary>
    private static void CollectConditionChain(DataEdgeInfo info, ReverseConversionContext ctx,
        HashSet<string> pubVars)
    {
        if (!string.IsNullOrEmpty(info.PubVarName))
            pubVars.Add(info.PubVarName);

        if (info.SourceNode.NodeType is BlueprintNodeType.CallHelper or BlueprintNodeType.Call)
        {
            foreach (var pin in info.SourceNode.InputPins)
            {
                if (pin.Name == Exec) continue;
                if (ctx.InputDataMap.TryGetValue((info.SourceNode.Id, pin.Name), out var argInfo))
                {
                    CollectConditionChain(argInfo, ctx, pubVars);
                }
            }
        }
    }

    private static ExpressionStatement CloneStatement(ExpressionStatement source) => new()
    {
        Expression = source.Expression,
        SourceCode = source.SourceCode,
        LineNumber = source.LineNumber
    };
}
