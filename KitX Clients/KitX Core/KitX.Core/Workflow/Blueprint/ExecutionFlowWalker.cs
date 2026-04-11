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
    private readonly Dictionary<string, INodeExportStrategy> _builtinFunctionStrategies;
    private readonly NodeExportHelper _exportHelper;

    public ExecutionFlowWalker(
        Dictionary<BlueprintNodeType, INodeExportStrategy> strategies,
        Dictionary<string, INodeExportStrategy> builtinFunctionStrategies,
        NodeExportHelper exportHelper)
    {
        _strategies = strategies;
        _builtinFunctionStrategies = builtinFunctionStrategies;
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
    /// and inserts ToLoopCond statements.
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

        // Detect loopback edges: if an exec connection goes from a scope's last node
        // to a scope containing a Loop node (and no ToLoopCond was generated),
        // insert a ToLoopCond statement. This is a fallback for when ToLoopCond
        // nodes are absent (e.g. Canvas round-trip losing BuiltinFunctionNode metadata).
        DetectAndInsertLoopbackToLoopConds(blueprint, ctx);
    }

    // ─── Node Walking ────────────────────────────────────────────────────

    private void WalkNode(BlueprintNode node, BlockDefinition currentBlock,
        ReverseConversionContext ctx, HashSet<string> visited, string? loopbackTargetId)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);

        if (loopbackTargetId != null && node.Id == loopbackTargetId)
        {
            var ownerName = ctx.LoopOwnerBlockNames.TryGetValue(loopbackTargetId, out var n) ? n : null;
            currentBlock.Statements.Add(CreateToLoopCondStatement(ownerName));
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
                {
                    ctx.LoopNodes[node.Id] = node;
                    ctx.LoopOwnerBlockNames[node.Id] = currentBlock.Name;
                }
            }
        }

        // If this node generated a ToLoopCond statement, don't follow exec chain
        if (stmt is FlowControlStatement { ControlType: FlowControlType.ToLoopCond })
            return;

        if (IsBranchNode(node) || IsLoopNode(node))
        {
            ctx.PendingControlFlowNodes.Add(node);
            return;
        }

        var execOut = node.OutputPins.FirstOrDefault(p => p.Name == Exec);
        if (execOut == null)
        {
            if (loopbackTargetId != null)
            {
                var ownerName2 = ctx.LoopOwnerBlockNames.TryGetValue(loopbackTargetId, out var n2) ? n2 : null;
                currentBlock.Statements.Add(CreateToLoopCondStatement(ownerName2));
            }
            return;
        }

        var execConn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == execOut.Id);
        if (execConn == null)
        {
            if (loopbackTargetId != null)
            {
                var ownerName3 = ctx.LoopOwnerBlockNames.TryGetValue(loopbackTargetId, out var n3) ? n3 : null;
                currentBlock.Statements.Add(CreateToLoopCondStatement(ownerName3));
            }
            return;
        }

        var nextNode = ctx.Blueprint.GetNodeById(execConn.TargetNodeId);
        if (nextNode == null) return;

        if (loopbackTargetId != null && nextNode.Id == loopbackTargetId)
        {
            var ownerName4 = ctx.LoopOwnerBlockNames.TryGetValue(loopbackTargetId, out var n4) ? n4 : null;
            currentBlock.Statements.Add(CreateToLoopCondStatement(ownerName4));
            return;
        }

        WalkNode(nextNode, currentBlock, ctx, visited, loopbackTargetId);
    }

    // ─── Statement Generation ────────────────────────────────────────────

    private static FlowControlStatement CreateToLoopCondStatement(string? returnTo = null)
    {
        var stmt = new FlowControlStatement
        {
            ControlType = FlowControlType.ToLoopCond,
            LineNumber = 1,
            ToLoopCondReturnTo = returnTo
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
            case BlueprintNodeType.BuiltinFunction when IsBreakNode(node):
                return new FlowControlStatement
                {
                    ControlType = FlowControlType.Break,
                    SourceCode = "Break();",
                    LineNumber = 1
                };
            case BlueprintNodeType.Get:
                return GenerateGetStatementViaStrategy(node, ctx);
            case BlueprintNodeType.BuiltinFunction when IsGetNode(node):
                return GenerateGetStatementViaStrategy(node, ctx);
            case BlueprintNodeType.Call:
            {
                if (node is not CallNode call) return null;
                var callArgs = _exportHelper.GetInputArgs(call);
                var funcRef = string.IsNullOrEmpty(call.PluginName)
                    ? call.FunctionName : $"{call.PluginName}.{call.FunctionName}";
                var sourceCode = $"{funcRef}({callArgs})";
                var callStmt = new ExpressionStatement
                {
                    Expression = sourceCode,
                    SourceCode = sourceCode + ";",
                    LineNumber = 1
                };
                PostProcessCallReturn(node, callStmt, ctx);
                return callStmt;
            }
            case BlueprintNodeType.CallHelper:
            {
                if (node is not CallHelperNode callHelper) return null;
                var helperArgs = _exportHelper.GetInputArgs(callHelper);
                var expression = $"{callHelper.HelperFunctionName}({helperArgs})";
                var helperStmt = new ExpressionStatement
                {
                    Expression = expression,
                    SourceCode = expression + ";",
                    LineNumber = 1
                };
                PostProcessCallReturn(node, helperStmt, ctx);
                return helperStmt;
            }
            case BlueprintNodeType.BuiltinFunction:
            {
                // Look up strategy by function name (BuiltinFunction nodes share one NodeType)
                if (node is BuiltinFunctionNode bfNode
                    && _builtinFunctionStrategies.TryGetValue(bfNode.FunctionName, out var bfStrategy))
                    return bfStrategy.ToStatement(node, _exportHelper);
                return null;
            }
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
        PostProcessCallReturn(node, stmt, ctx);

        return stmt;
    }

    /// <summary>
    /// Post-processes Call/CallHelper statements to add PubVar prefix when Return pin is consumed.
    /// </summary>
    private void PostProcessCallReturn(BlueprintNode node, BlockStatement stmt, ReverseConversionContext ctx)
    {
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
                    case BlueprintNodeType.BuiltinFunction when IsBranchNode(node):
                        ProcessBranchSubGraphs(node, ctx, processedTargets);
                        break;
                    case BlueprintNodeType.Loop:
                    case BlueprintNodeType.BuiltinFunction when IsLoopNode(node):
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

    // ─── BlockScope ToLoopCond Helpers ───────────────────────────────────

    private static (BlueprintNode? loopNode, List<BlueprintNode> path) FollowExecChainToLoop(
        BlueprintNode startNode, ReverseConversionContext ctx)
    {
        var path = new List<BlueprintNode>();
        var current = startNode;
        var visited = new HashSet<string>();

        while (current != null && !visited.Contains(current.Id))
        {
            visited.Add(current.Id);

            if (IsLoopNode(current))
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

    private static string? FindScopeContainingNode(string nodeId, Contract.Workflow.Blueprint bp)
    {
        foreach (var scope in bp.BlockScopes)
        {
            if (scope.NodeIds.Contains(nodeId))
                return scope.Name;
        }
        return null;
    }

    // ─── BuiltinFunction Node Type Helpers ─────────────────────────────────

    /// <summary>
    /// Checks if a node represents the given logical type, accounting for both
    /// legacy typed nodes (e.g. LoopNode) and registry-based BuiltinFunctionNode
    /// with the equivalent function name.
    /// </summary>
    private static bool IsNodeType(BlueprintNode node, BlueprintNodeType type) => node.NodeType == type
        || (node is BuiltinFunctionNode bfn && bfn.FunctionName == type.ToString());

    private static bool IsBranchNode(BlueprintNode node) =>
        IsNodeType(node, BlueprintNodeType.Branch);

    private static bool IsLoopNode(BlueprintNode node) =>
        IsNodeType(node, BlueprintNodeType.Loop);

    private static bool IsBreakNode(BlueprintNode node) =>
        IsNodeType(node, BlueprintNodeType.Break);

    private static bool IsGetNode(BlueprintNode node) =>
        IsNodeType(node, BlueprintNodeType.Get);

    // ─── Loopback Edge Detection (BlockScopes fallback) ──────────────────

    /// <summary>
    /// Detects exec connections that form loopback edges (from one scope's tail
    /// to a scope containing a Loop node) and inserts ToLoopCond statements
    /// when none was generated. This is a fallback for when ToLoopCond nodes
    /// are absent or lost during Canvas round-trips.
    /// </summary>
    private static void DetectAndInsertLoopbackToLoopConds(
        Contract.Workflow.Blueprint blueprint, ReverseConversionContext ctx)
    {
        // Build: LoopNodeId → ScopeName (which scope contains each Loop node)
        var loopNodeToScope = new Dictionary<string, string>();
        foreach (var scope in blueprint.BlockScopes)
        {
            foreach (var nodeId in scope.NodeIds)
            {
                if (ctx.LoopNodes.ContainsKey(nodeId))
                    loopNodeToScope[nodeId] = scope.Name;
            }
        }

        if (loopNodeToScope.Count == 0) return;

        // Build: ScopeName → set of first node IDs
        var scopeFirstNodes = new Dictionary<string, HashSet<string>>();
        foreach (var scope in blueprint.BlockScopes)
        {
            if (scope.NodeIds.Count == 0) continue;
            scopeFirstNodes[scope.Name] = [scope.NodeIds[0]];
        }

        // Build: ScopeName → scope's block definition
        var scopeToBlock = new Dictionary<string, BlockDefinition>();
        if (ctx.Script.MainBlock != null)
            scopeToBlock[BlockScriptWellKnown.Blocks.MainBlock] = ctx.Script.MainBlock;
        foreach (var kvp in ctx.Script.NamedBlocks)
            scopeToBlock[kvp.Key] = kvp.Value;

        // For each scope, check if its last node has an exec connection
        // leading to a node inside a Loop-containing scope.
        // Also check if the scope already ends with a ToLoopCond statement.
        foreach (var scope in blueprint.BlockScopes)
        {
            if (scope.NodeIds.Count == 0) continue;
            if (!scopeToBlock.TryGetValue(scope.Name, out var block)) continue;

            // Skip scopes that already end with ToLoopCond
            if (block.Statements.LastOrDefault() is FlowControlStatement { ControlType: FlowControlType.ToLoopCond })
                continue;

            // Find the last node in this scope that has an exec output connection
            string? lastNodeId = null;
            for (int i = scope.NodeIds.Count - 1; i >= 0; i--)
            {
                var nodeId = scope.NodeIds[i];
                if (!ctx.NodeById.TryGetValue(nodeId, out var node)) continue;
                var execOut = node.OutputPins.FirstOrDefault(p => p.Name == Exec);
                if (execOut == null) continue;
                var execConn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == execOut.Id);
                if (execConn != null)
                {
                    lastNodeId = nodeId;
                    break;
                }
            }

            if (lastNodeId == null) continue;
            if (!ctx.NodeById.TryGetValue(lastNodeId, out var lastNode)) continue;

            var lastExecOut = lastNode.OutputPins.First(p => p.Name == Exec);
            var lastExecConn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == lastExecOut.Id);
            if (lastExecConn == null) continue;

            var targetNode = blueprint.GetNodeById(lastExecConn.TargetNodeId);
            if (targetNode == null) continue;

            // Check: does the target node belong to a scope that contains a Loop?
            var targetScopeName = FindScopeContainingNode(targetNode.Id, blueprint);
            if (targetScopeName == null) continue;

            // Check: does that scope contain a Loop node?
            var targetScope = blueprint.BlockScopes.FirstOrDefault(s => s.Name == targetScopeName);
            if (targetScope == null) continue;

            bool targetScopeContainsLoop = targetScope.NodeIds
                .Any(nid => ctx.LoopNodes.ContainsKey(nid));
            if (!targetScopeContainsLoop) continue;

            // Skip if the target scope is the same as the source scope
            if (targetScopeName == scope.Name) continue;

            // Found a loopback edge! Insert ToLoopCond pointing to the Loop's scope.
            block.Statements.Add(CreateToLoopCondStatement(targetScopeName));
            Log.Debug("[ExecutionFlowWalker] Inserted fallback ToLoopCond(\"{Target}\") in scope \"{Scope}\"",
                targetScopeName, scope.Name);
        }
    }
}
