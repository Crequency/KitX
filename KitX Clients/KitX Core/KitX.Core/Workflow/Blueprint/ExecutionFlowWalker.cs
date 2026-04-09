using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.BlockScripting;
using KitX.Core.Workflow.Blueprint.ReversePipeline;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Phase 2 of bp2bs: walks the Blueprint execution flow graph and generates
/// BlockScript statements. Handles both BlockScopes-based and topology-based paths.
/// Extracted from BlueprintToBlockScriptConverter for single-responsibility.
/// </summary>
internal class ExecutionFlowWalker
{
    private readonly Dictionary<BlueprintNodeType, INodeExportStrategy> _strategies;
    private readonly NodeExportHelper _exportHelper;

    public ExecutionFlowWalker(
        Dictionary<BlueprintNodeType, INodeExportStrategy> strategies,
        NodeExportHelper exportHelper)
    {
        _strategies = strategies;
        _exportHelper = exportHelper;
    }

    /// <summary>
    /// Walks execution flow starting from Entry node (topology-based path).
    /// Creates MainBlock and walks the execution graph depth-first.
    /// </summary>
    public void WalkExecutionFlow(ReverseConversionContext ctx)
    {
        var entryNode = ctx.Blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
        if (entryNode == null)
        {
            Log.Warning("[ExecutionFlowWalker] No Entry node found");
            return;
        }

        var mainBlock = new BlockDefinition { Type = BlockType.MainBlock, Name = BlockScriptWellKnown.Blocks.MainBlock };
        ctx.Script.MainBlock = mainBlock;

        var visited = new HashSet<string>();
        WalkNode(entryNode, mainBlock, ctx, visited, loopbackTargetId: null);

        ProcessSubGraphs(ctx);
    }

    /// <summary>
    /// Generates statements from stored BlockScopes (BlockScopes-based path).
    /// Processes each scope, generates statements for its nodes, resolves control flow,
    /// and inserts LoopBodyEnd statements.
    /// </summary>
    public void WalkFromBlockScopes(Contract.Workflow.Blueprint blueprint, ReverseConversionContext ctx)
    {
        // Build scopesByName lookup
        foreach (var scope in blueprint.BlockScopes)
            ctx.ScopesByName[scope.Name] = scope;

        // Generate statements from stored block membership
        foreach (var scope in blueprint.BlockScopes)
        {
            BlockDefinition block;
            if (scope.IsMainBlock)
            {
                block = new BlockDefinition { Type = BlockType.MainBlock, Name = BlockScriptWellKnown.Blocks.MainBlock };
                ctx.Script.MainBlock = block;
            }
            else
            {
                block = new BlockDefinition { Type = BlockType.NamedBlock, Name = scope.Name };
                ctx.Script.NamedBlocks[scope.Name] = block;
            }

            foreach (var nodeId in scope.NodeIds)
            {
                if (!ctx.NodeById.TryGetValue(nodeId, out var node)) continue;
                var stmt = GenerateStatement(node, ctx);
                if (stmt != null)
                {
                    block.Statements.Add(stmt);
                    if (stmt is FlowControlStatement flow)
                    {
                        ctx.ControlFlowMap[node.Id] = flow;
                        if (flow.ControlType == FlowControlType.Loop)
                            ctx.LoopNodes[node.Id] = node;
                    }
                }
            }
        }

        // Resolve control flow block names from stored ownership
        foreach (var scope in blueprint.BlockScopes)
        {
            if (scope.OwnerNodeId == null) continue;
            if (!ctx.ControlFlowMap.TryGetValue(scope.OwnerNodeId, out var flow)) continue;
            if (scope.OwnerArmName == BlockScriptWellKnown.Pins.True || scope.OwnerArmName == BlockScriptWellKnown.Pins.LoopBody)
                flow.TrueBlockName = scope.Name;
            else if (scope.OwnerArmName == BlockScriptWellKnown.Pins.False || scope.OwnerArmName == BlockScriptWellKnown.Pins.LoopEnd)
                flow.FalseBlockName = scope.Name;
        }

        foreach (var flow in ctx.ControlFlowMap.Values)
            flow.RegenerateSourceCode();

        DetectAndInsertLoopBodyEnds(blueprint, ctx);
    }

    // ─── Node Walking ────────────────────────────────────────────────────

    private void WalkNode(BlueprintNode node, BlockDefinition currentBlock,
        ReverseConversionContext ctx, HashSet<string> visited, string? loopbackTargetId)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);

        if (loopbackTargetId != null && node.Id == loopbackTargetId)
        {
            currentBlock.Statements.Add(CreateLoopBodyEndStatement());
            return;
        }

        var stmt = GenerateStatement(node, ctx);
        if (stmt != null)
        {
            currentBlock.Statements.Add(stmt);
            if (stmt is FlowControlStatement flow)
            {
                ctx.ControlFlowMap[node.Id] = flow;
                if (flow.ControlType == FlowControlType.Loop)
                    ctx.LoopNodes[node.Id] = node;
            }
        }

        if (node.NodeType is BlueprintNodeType.Branch or BlueprintNodeType.Loop)
        {
            ctx.PendingControlFlowNodes.Add(node);
            return;
        }

        var execOut = node.OutputPins.FirstOrDefault(p => p.Name == Exec);
        if (execOut == null)
        {
            if (loopbackTargetId != null)
                currentBlock.Statements.Add(CreateLoopBodyEndStatement());
            return;
        }

        var execConn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == execOut.Id);
        if (execConn == null)
        {
            if (loopbackTargetId != null)
                currentBlock.Statements.Add(CreateLoopBodyEndStatement());
            return;
        }

        var nextNode = ctx.Blueprint.GetNodeById(execConn.TargetNodeId);
        if (nextNode == null) return;

        if (loopbackTargetId != null && nextNode.Id == loopbackTargetId)
        {
            currentBlock.Statements.Add(CreateLoopBodyEndStatement());
            return;
        }

        WalkNode(nextNode, currentBlock, ctx, visited, loopbackTargetId);
    }

    // ─── Statement Generation ────────────────────────────────────────────

    private static FlowControlStatement CreateLoopBodyEndStatement(string? returnTo = null)
    {
        var stmt = new FlowControlStatement
        {
            ControlType = FlowControlType.LoopBodyEnd,
            LineNumber = 1,
            LoopBodyEndReturnTo = returnTo
        };
        stmt.RegenerateSourceCode();
        return stmt;
    }

    /// <summary>
    /// Generates a BlockStatement for the given node using Export Strategies,
    /// with post-processing for PubVar assignments.
    /// </summary>
    private BlockStatement? GenerateStatement(BlueprintNode node, ReverseConversionContext ctx)
    {
        switch (node.NodeType)
        {
            case BlueprintNodeType.Entry:
                return null;
            case BlueprintNodeType.Break:
                return new FlowControlStatement
                {
                    ControlType = FlowControlType.Break,
                    SourceCode = "Break();",
                    LineNumber = 1
                };
            case BlueprintNodeType.Get:
                return GenerateGetStatementViaStrategy(node, ctx);
            default:
                break;
        }

        if (!_strategies.TryGetValue(node.NodeType, out var strategy))
        {
            Log.Warning("[ExecutionFlowWalker] Unhandled node type: {NodeType}", node.NodeType);
            return null;
        }

        var stmt = strategy.ToStatement(node, _exportHelper);
        if (stmt == null) return null;

        // Post-process: add PubVar prefix for Call/CallHelper with consumed Return
        if (node.NodeType is BlueprintNodeType.Call or BlueprintNodeType.CallHelper
            && stmt is ExpressionStatement exprStmt)
        {
            var returnPin = node.OutputPins.FirstOrDefault(p => p.Name == Return);
            bool hasReturn = returnPin != null && ctx.ConsumedOutputs.Contains((node.Id, Return));
            if (hasReturn)
            {
                var pubVar = NodeExportHelper.FindOutputPubVar(node, Return, ctx);
                if (pubVar != null)
                    exprStmt.SourceCode = $"{pubVar} = {exprStmt.Expression};";
            }
        }

        return stmt;
    }

    private BlockStatement? GenerateGetStatementViaStrategy(BlueprintNode node, ReverseConversionContext ctx)
    {
        if (!ctx.ConsumedOutputs.Contains((node.Id, Value)))
            return null;

        var pubVar = NodeExportHelper.FindOutputPubVar(node, Value, ctx);
        if (pubVar == null) return null;

        if (!_strategies.TryGetValue(BlueprintNodeType.Get, out var strategy))
            return null;

        var stmt = strategy.ToStatement(node, _exportHelper);
        if (stmt is ExpressionStatement exprStmt)
        {
            exprStmt.SourceCode = $"{pubVar} = {exprStmt.Expression};";
            return exprStmt;
        }
        return stmt;
    }

    // ─── Sub-graph Processing ────────────────────────────────────────────

    private void ProcessSubGraphs(ReverseConversionContext ctx)
    {
        var processedTargets = new HashSet<string>();
        var processedNodes = new HashSet<string>();

        while (ctx.PendingControlFlowNodes.Count > 0)
        {
            var pendingNodes = ctx.PendingControlFlowNodes.ToList();
            ctx.PendingControlFlowNodes.Clear();

            foreach (var node in pendingNodes)
            {
                if (processedNodes.Contains(node.Id)) continue;
                processedNodes.Add(node.Id);

                switch (node.NodeType)
                {
                    case BlueprintNodeType.Branch:
                        ProcessBranchSubGraphs(node, ctx, processedTargets);
                        break;
                    case BlueprintNodeType.Loop:
                        ProcessLoopSubGraphs(node, ctx, processedTargets);
                        break;
                }
            }
        }

        foreach (var kvp in ctx.ControlFlowMap)
        {
            var flow = kvp.Value;
            if (ctx.BlockNameAssignments.TryGetValue(kvp.Key, out var assignments))
            {
                flow.TrueBlockName = assignments.TrueBlockName;
                flow.FalseBlockName = assignments.FalseBlockName;
                flow.RegenerateSourceCode();
            }
        }
    }

    private void ProcessBranchSubGraphs(BlueprintNode branchNode, ReverseConversionContext ctx,
        HashSet<string> processedTargets)
    {
        var trueBlockName = string.Empty;
        var falseBlockName = string.Empty;
        var currentLoopback = ctx.CurrentLoopbackTargetId;

        foreach (var pinName in new[] { True, False })
        {
            var pin = branchNode.OutputPins.FirstOrDefault(p => p.Name == pinName);
            if (pin == null) continue;

            var conn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == pin.Id);
            if (conn == null || processedTargets.Contains(conn.TargetNodeId)) continue;

            var targetNode = ctx.Blueprint.GetNodeById(conn.TargetNodeId);
            if (targetNode == null) continue;

            var blockName = $"Block_{ctx.BlockCounter++}";
            var block = new BlockDefinition { Type = BlockType.NamedBlock, Name = blockName };

            WalkNode(targetNode, block, ctx, new HashSet<string>(), loopbackTargetId: currentLoopback);

            ctx.Script.NamedBlocks[blockName] = block;
            processedTargets.Add(conn.TargetNodeId);

            if (pinName == True) trueBlockName = blockName;
            else falseBlockName = blockName;
        }

        ctx.BlockNameAssignments[branchNode.Id] = (trueBlockName, falseBlockName);
    }

    private void ProcessLoopSubGraphs(BlueprintNode loopNode, ReverseConversionContext ctx,
        HashSet<string> processedTargets)
    {
        var loopBodyBlockName = string.Empty;
        var loopEndBlockName = string.Empty;

        var loopBodyPin = loopNode.OutputPins.FirstOrDefault(p => p.Name == LoopBody);
        if (loopBodyPin != null)
        {
            var conn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == loopBodyPin.Id);
            if (conn != null && !processedTargets.Contains(conn.TargetNodeId))
            {
                var targetNode = ctx.Blueprint.GetNodeById(conn.TargetNodeId);
                if (targetNode != null)
                {
                    loopBodyBlockName = $"Block_{ctx.BlockCounter++}";
                    var block = new BlockDefinition { Type = BlockType.NamedBlock, Name = loopBodyBlockName };

                    ctx.CurrentLoopbackTargetId = loopNode.Id;
                    WalkNode(targetNode, block, ctx, new HashSet<string>(), loopbackTargetId: loopNode.Id);

                    ctx.Script.NamedBlocks[loopBodyBlockName] = block;
                    processedTargets.Add(conn.TargetNodeId);
                    ctx.LoopBodyBlocks[loopNode.Id] = block;
                }
            }
        }

        var loopEndPin = loopNode.OutputPins.FirstOrDefault(p => p.Name == LoopEnd);
        if (loopEndPin != null)
        {
            var conn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == loopEndPin.Id);
            if (conn != null && !processedTargets.Contains(conn.TargetNodeId))
            {
                var targetNode = ctx.Blueprint.GetNodeById(conn.TargetNodeId);
                if (targetNode != null)
                {
                    loopEndBlockName = $"Block_{ctx.BlockCounter++}";
                    var block = new BlockDefinition { Type = BlockType.NamedBlock, Name = loopEndBlockName };

                    WalkNode(targetNode, block, ctx, new HashSet<string>(), loopbackTargetId: null);

                    ctx.Script.NamedBlocks[loopEndBlockName] = block;
                    processedTargets.Add(conn.TargetNodeId);
                }
            }
        }

        ctx.BlockNameAssignments[loopNode.Id] = (loopBodyBlockName, loopEndBlockName);
    }

    // ─── BlockScope LoopBodyEnd Helpers ───────────────────────────────────

    private void DetectAndInsertLoopBodyEnds(Contract.Workflow.Blueprint bp, ReverseConversionContext ctx)
    {
        foreach (var scope in bp.BlockScopes)
        {
            if (scope.NodeIds.Count == 0) continue;
            if (scope.NodeIds.Any(id => ctx.NodeById.TryGetValue(id, out var n) && n.NodeType == BlueprintNodeType.Loop))
                continue;

            var lastNodeId = scope.NodeIds[scope.NodeIds.Count - 1];
            if (!ctx.NodeById.TryGetValue(lastNodeId, out var lastNode)) continue;

            var (loopNode, _) = FollowExecChainToLoop(lastNode, ctx);
            if (loopNode == null) continue;

            var loopScopeName = FindScopeContainingNode(loopNode.Id, bp);
            if (loopScopeName == scope.Name) continue;

            var returnToBlock = loopScopeName;
            if (returnToBlock == null) continue;

            BlockDefinition? blockDef = scope.IsMainBlock
                ? ctx.Script.MainBlock
                : ctx.Script.NamedBlocks.GetValueOrDefault(scope.Name);
            if (blockDef == null) continue;

            GenerateConditionStatements(loopNode, blockDef, ctx);
            blockDef.Statements.Add(CreateLoopBodyEndStatement(returnToBlock));
        }
    }

    private static (BlueprintNode? loopNode, List<BlueprintNode> path) FollowExecChainToLoop(
        BlueprintNode startNode, ReverseConversionContext ctx)
    {
        var path = new List<BlueprintNode>();
        var current = startNode;
        var visited = new HashSet<string>();

        while (current != null && !visited.Contains(current.Id))
        {
            visited.Add(current.Id);

            if (current.NodeType == BlueprintNodeType.Loop)
                return (current, path);

            var execOutPin = current.OutputPins.FirstOrDefault(p => p.Name == Exec);
            if (execOutPin == null) break;

            var execConn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == execOutPin.Id);
            if (execConn == null) break;

            if (!ctx.NodeById.TryGetValue(execConn.TargetNodeId, out var next)) break;

            path.Add(next);
            current = next;
        }

        return (null, path);
    }

    private void GenerateConditionStatements(BlueprintNode loopNode, BlockDefinition blockDef,
        ReverseConversionContext ctx)
    {
        var condPin = loopNode.InputPins.FirstOrDefault(p => p.Name == Condition);
        if (condPin == null) return;

        var condConn = ctx.DataConnections.FirstOrDefault(c => c.TargetPinId == condPin.Id);
        if (condConn == null) return;

        var generated = new HashSet<string>();
        GenerateDataChainStatements(condConn.SourceNodeId, blockDef, ctx, generated);
    }

    private void GenerateDataChainStatements(string sourceNodeId,
        BlockDefinition blockDef, ReverseConversionContext ctx, HashSet<string> generated)
    {
        if (generated.Contains(sourceNodeId)) return;
        if (!ctx.NodeById.TryGetValue(sourceNodeId, out var sourceNode)) return;

        foreach (var inputPin in sourceNode.InputPins)
        {
            if (inputPin.Name == Exec) continue;
            var upConn = ctx.DataConnections.FirstOrDefault(c => c.TargetPinId == inputPin.Id);
            if (upConn != null)
                GenerateDataChainStatements(upConn.SourceNodeId, blockDef, ctx, generated);
        }

        if (sourceNode.NodeType == BlueprintNodeType.Const) return;

        generated.Add(sourceNodeId);

        var stmt = GenerateStatement(sourceNode, ctx);
        if (stmt != null)
            blockDef.Statements.Add(stmt);
    }

    private static string? FindScopeContainingNode(string nodeId, Contract.Workflow.Blueprint bp)
    {
        foreach (var scope in bp.BlockScopes)
        {
            if (scope.NodeIds.Contains(nodeId))
                return scope.Name;
        }
        return null;
    }
}
