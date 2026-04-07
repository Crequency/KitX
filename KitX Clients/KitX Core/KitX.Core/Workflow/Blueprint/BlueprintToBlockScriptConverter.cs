using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint.Pipeline;
using KitX.Core.Workflow.Blueprint.ReversePipeline;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

/// <summary>
/// Converts Blueprint back to a fully-expanded BlockScript source code.
/// Thin orchestrator that delegates to pipeline phases:
///   Phase 1: BlueprintAnalyzer — data index construction
///   Phase 2: Walk execution flow (BlockScopes or topology path)
///   Phase 3: ConditionDuplicator — loop condition duplication
///   Phase 4: BlockScriptAssembler — source code assembly
/// Also implements INodeExportHelper for use by Export Strategies.
/// </summary>
public class BlueprintToBlockScriptConverter : IBlueprintToBlockScriptConverter, INodeExportHelper
{
    private readonly Dictionary<BlueprintNodeType, INodeExportStrategy> _strategies;
    private readonly BlueprintAnalyzer _analyzer = new();
    private readonly ConditionDuplicator _conditionDuplicator = new();
    private readonly BlockScriptAssembler _assembler = new();

    /// <summary>
    /// Current conversion context — set during ConvertToBlockScript, accessible by INodeExportHelper methods.
    /// </summary>
    private ReverseConversionContext? _currentCtx;

    public BlueprintToBlockScriptConverter(IEnumerable<INodeExportStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(s => s.NodeType);
    }

    /// <inheritdoc/>
    public Contract.Workflow.Blueprint Blueprint { get; private set; } = null!;

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public string Convert(Contract.Workflow.Blueprint blueprint)
        => ConvertToBlockScript(blueprint).SourceCode;

    /// <inheritdoc/>
    public BlockScript ConvertToBlockScript(Contract.Workflow.Blueprint blueprint)
    {
        if (blueprint.BlockScopes.Count > 0)
            return ConvertWithBlockScopes(blueprint);
        return ConvertWithTopology(blueprint);
    }

    // ──────────────────────────────────────────────
    // Conversion paths
    // ──────────────────────────────────────────────

    private BlockScript ConvertWithBlockScopes(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;
        var ctx = new ReverseConversionContext { Blueprint = blueprint, Script = new BlockScript() };
        _currentCtx = ctx;

        // Phase 1
        _analyzer.Analyze(ctx);

        // Build scopesByName lookup
        foreach (var scope in blueprint.BlockScopes)
            ctx.ScopesByName[scope.Name] = scope;

        // Phase 2: Generate statements from stored block membership
        foreach (var scope in blueprint.BlockScopes)
        {
            BlockDefinition block;
            if (scope.IsMainBlock)
            {
                block = new BlockDefinition { Type = BlockType.MainBlock, Name = "MainBlock" };
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
            if (scope.OwnerArmName == "True" || scope.OwnerArmName == "LoopBody")
                flow.TrueBlockName = scope.Name;
            else if (scope.OwnerArmName == "False" || scope.OwnerArmName == "LoopEnd")
                flow.FalseBlockName = scope.Name;
        }

        foreach (var flow in ctx.ControlFlowMap.Values)
            flow.RegenerateSourceCode();

        DetectAndInsertLoopBodyEnds(blueprint, ctx);

        // Phase 4
        _assembler.Assemble(ctx);
        return ctx.Script;
    }

    private BlockScript ConvertWithTopology(Contract.Workflow.Blueprint blueprint)
    {
        Blueprint = blueprint;
        var ctx = new ReverseConversionContext { Blueprint = blueprint, Script = new BlockScript() };
        _currentCtx = ctx;

        // Phase 1
        _analyzer.Analyze(ctx);

        // Phase 2: Walk execution flow
        WalkExecutionFlow(ctx);

        // Phase 3: Duplicate loop conditions
        _conditionDuplicator.Duplicate(ctx);

        // Phase 4: Assemble source code
        _assembler.Assemble(ctx);
        return ctx.Script;
    }

    // ──────────────────────────────────────────────
    // INodeExportHelper
    // ──────────────────────────────────────────────

    /// <inheritdoc/>
    public string GetInputValue(BlueprintNode node, string pinName)
    {
        var pin = node.InputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin == null) return string.Empty;

        var dataConn = Blueprint.Connections.FirstOrDefault(c => c.TargetPinId == pin.Id);
        if (dataConn == null) return FormatLiteralValue(pin.DefaultValue ?? string.Empty, _currentCtx);

        var sourceNode = Blueprint.GetNodeById(dataConn.SourceNodeId);
        if (sourceNode is ConstNode constNode)
            return constNode.ConstName;

        return dataConn.PubVarName ?? FormatLiteralValue(pin.DefaultValue ?? string.Empty, _currentCtx);
    }

    /// <inheritdoc/>
    public string GetInputArgs(BlueprintNode node)
    {
        var args = new List<string>();
        foreach (var pin in node.InputPins)
        {
            if (pin.Name != "Exec")
                args.Add(GetInputValue(node, pin.Name));
        }
        return string.Join(", ", args);
    }

    // ──────────────────────────────────────────────
    // Phase 2: Walk Execution Flow (topology path)
    // ──────────────────────────────────────────────

    private void WalkExecutionFlow(ReverseConversionContext ctx)
    {
        var entryNode = ctx.Blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
        if (entryNode == null)
        {
            Log.Warning("[BlueprintToScript] No Entry node found");
            return;
        }

        var mainBlock = new BlockDefinition { Type = BlockType.MainBlock, Name = "MainBlock" };
        ctx.Script.MainBlock = mainBlock;

        var visited = new HashSet<string>();
        WalkNode(entryNode, mainBlock, ctx, visited, loopbackTargetId: null);

        ProcessSubGraphs(ctx);
    }

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

        var execOut = node.OutputPins.FirstOrDefault(p => p.Name == "Exec");
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

    // ──────────────────────────────────────────────
    // Statement generation (strategy dispatch)
    // ──────────────────────────────────────────────

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
            Log.Warning("[BlueprintToScript] Unhandled node type: {NodeType}", node.NodeType);
            return null;
        }

        var stmt = strategy.ToStatement(node, this);
        if (stmt == null) return null;

        // Post-process: add PubVar prefix for Call/CallHelper with consumed Return
        if (node.NodeType is BlueprintNodeType.Call or BlueprintNodeType.CallHelper
            && stmt is ExpressionStatement exprStmt)
        {
            var returnPin = node.OutputPins.FirstOrDefault(p => p.Name == "Return");
            bool hasReturn = returnPin != null && ctx.ConsumedOutputs.Contains((node.Id, "Return"));
            if (hasReturn)
            {
                var pubVar = FindOutputPubVar(node, "Return", ctx);
                if (pubVar != null)
                    exprStmt.SourceCode = $"{pubVar} = {exprStmt.Expression};";
            }
        }

        return stmt;
    }

    private BlockStatement? GenerateGetStatementViaStrategy(BlueprintNode node, ReverseConversionContext ctx)
    {
        if (!ctx.ConsumedOutputs.Contains((node.Id, "Value")))
            return null;

        var pubVar = FindOutputPubVar(node, "Value", ctx);
        if (pubVar == null) return null;

        if (!_strategies.TryGetValue(BlueprintNodeType.Get, out var strategy))
            return null;

        var stmt = strategy.ToStatement(node, this);
        if (stmt is ExpressionStatement exprStmt)
        {
            exprStmt.SourceCode = $"{pubVar} = {exprStmt.Expression};";
            return exprStmt;
        }
        return stmt;
    }

    // ──────────────────────────────────────────────
    // Input Resolution
    // ──────────────────────────────────────────────

    private string ResolveInputValue(BlueprintNode node, string pinName, ReverseConversionContext ctx)
    {
        var pin = node.InputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin == null) return string.Empty;

        if (ctx.InputDataMap.TryGetValue((node.Id, pin.Name), out var info))
        {
            if (info.SourceNode is ConstNode constNode)
                return constNode.ConstName;
            if (!string.IsNullOrEmpty(info.PubVarName))
                return info.PubVarName;
        }

        var defaultValue = pin.DefaultValue ?? string.Empty;
        return FormatLiteralValue(defaultValue, ctx);
    }

    private static string FormatLiteralValue(string value, ReverseConversionContext? ctx = null)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith("\"")) return value;
        if (ctx != null && ctx.AllPubVars.Contains(value)) return value;
        if (int.TryParse(value, out _) || double.TryParse(value, out _)) return value;
        if (value == "true" || value == "false") return value;
        if (value.Contains("(")) return value;
        if (ctx != null && ctx.Blueprint.Nodes.OfType<ConstNode>().Any(c => c.ConstName == value))
            return value;
        return $"\"{value}\"";
    }

    private string? FindOutputPubVar(BlueprintNode node, string pinName, ReverseConversionContext ctx)
    {
        var pin = node.OutputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin == null) return null;
        var conn = ctx.DataConnections.FirstOrDefault(c => c.SourcePinId == pin.Id);
        return conn?.PubVarName;
    }

    // ──────────────────────────────────────────────
    // Sub-graph Processing
    // ──────────────────────────────────────────────

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

        foreach (var pinName in new[] { "True", "False" })
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

            if (pinName == "True") trueBlockName = blockName;
            else falseBlockName = blockName;
        }

        ctx.BlockNameAssignments[branchNode.Id] = (trueBlockName, falseBlockName);
    }

    private void ProcessLoopSubGraphs(BlueprintNode loopNode, ReverseConversionContext ctx,
        HashSet<string> processedTargets)
    {
        var loopBodyBlockName = string.Empty;
        var loopEndBlockName = string.Empty;

        var loopBodyPin = loopNode.OutputPins.FirstOrDefault(p => p.Name == "LoopBody");
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

        var loopEndPin = loopNode.OutputPins.FirstOrDefault(p => p.Name == "LoopEnd");
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

    // ──────────────────────────────────────────────
    // BlockScope helpers
    // ──────────────────────────────────────────────

    private void DetectAndInsertLoopBodyEnds(Contract.Workflow.Blueprint bp, ReverseConversionContext ctx)
    {
        foreach (var scope in bp.BlockScopes)
        {
            if (scope.NodeIds.Count == 0) continue;

            var lastNodeId = scope.NodeIds[scope.NodeIds.Count - 1];
            if (!ctx.NodeById.TryGetValue(lastNodeId, out var lastNode)) continue;

            var execOutPin = lastNode.OutputPins.FirstOrDefault(p => p.Name == "Exec");
            if (execOutPin == null) continue;

            var execConn = ctx.ExecConnections.FirstOrDefault(c => c.SourcePinId == execOutPin.Id);
            if (execConn == null) continue;

            if (!ctx.NodeById.TryGetValue(execConn.TargetNodeId, out var targetNode)) continue;
            if (targetNode.NodeType != BlueprintNodeType.Loop) continue;

            var returnToBlock = FindScopeContainingNode(targetNode.Id, bp);
            if (returnToBlock == null) continue;

            BlockDefinition? blockDef = scope.IsMainBlock
                ? ctx.Script.MainBlock
                : ctx.Script.NamedBlocks.GetValueOrDefault(scope.Name);
            if (blockDef == null) continue;

            blockDef.Statements.Add(CreateLoopBodyEndStatement(returnToBlock));
        }
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
