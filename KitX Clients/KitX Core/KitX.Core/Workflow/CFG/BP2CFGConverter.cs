using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;
using KitX.Core.Workflow.Pipeline;

namespace KitX.Core.Workflow.CFG;

/// <summary>
/// Builds a <see cref="ControlFlowGraph"/> from a <see cref="Blueprint"/>.
/// This is the unified BP→BS algorithm.
///
/// Whether <see cref="Contract.Workflow.Blueprint.BlockScopes"/> is present or not,
/// this single algorithm produces a CFG. When BlockScopes are available, they guide
/// block membership; when absent, blocks are derived from topology alone.
/// </summary>
internal class BP2CFGConverter
{
    private readonly Dictionary<BlueprintNodeType, INodeExportStrategy> _strategies;
    private readonly Dictionary<string, INodeExportStrategy> _builtinFunctionStrategies;
    private readonly NodeExportHelper _exportHelper;

    public BP2CFGConverter(
        Dictionary<BlueprintNodeType, INodeExportStrategy> strategies,
        Dictionary<string, INodeExportStrategy> builtinFunctionStrategies,
        NodeExportHelper exportHelper)
    {
        _strategies = strategies;
        _builtinFunctionStrategies = builtinFunctionStrategies;
        _exportHelper = exportHelper;
    }

    /// <summary>
    /// Builds a ControlFlowGraph from a Blueprint.
    /// Single entry point that unifies the BlockScopes-based and topology-based paths.
    /// </summary>
    public ControlFlowGraph Build(Contract.Workflow.Blueprint blueprint)
    {
        var cfg = new ControlFlowGraph
        {
            DebugContext = new BlueprintDebugContext()
        };

        // ── Step 1: Index and classify all nodes and connections ──
        var nodeById = new Dictionary<string, BlueprintNode>();
        var execConns = new List<BlueprintConnection>();
        var dataConns = new List<BlueprintConnection>();
        var inputDataMap = new Dictionary<(string, string), DataEdgeInfo>();
        var consumedOutputs = new HashSet<(string, string)>();
        var allPubVars = new List<string>();
        var autoPubVars = new List<string>();
        int pubVarCounter = 1;

        AnalyzeConnections(blueprint, nodeById, execConns, dataConns, inputDataMap,
            consumedOutputs, allPubVars, autoPubVars, ref pubVarCounter);

        // Populate ConversionContext with analysis results for statement generation
        if (_currentCtx != null)
        {
            _currentCtx.NodeById = nodeById;
            _currentCtx.ExecConnections = execConns;
            _currentCtx.DataConnections = dataConns;
            _currentCtx.InputDataMap = inputDataMap;
            _currentCtx.ConsumedOutputs = consumedOutputs;
            _currentCtx.AllPubVars = allPubVars;
            _currentCtx.AutoPubVars = autoPubVars;
            _currentCtx.PubVarCounter = pubVarCounter;
        }

        // ── Step 2: Compute reachability from Entry ──
        var reachableNodeIds = FindReachableNodeIds(blueprint, nodeById, execConns);

        // ── Step 3: Build CFG blocks ──
        if (blueprint.BlockScopes.Count > 0)
            BuildBlocksFromScopes(blueprint, cfg, nodeById, reachableNodeIds, execConns);
        else
            BuildBlocksFromTopology(blueprint, cfg, nodeById, reachableNodeIds, execConns);

        // ── Step 3.5: Resolve Branch/Loop target block names ──
        // Strategy-generated statements don't know their target blocks; we resolve them
        // from Blueprint connections and the node→block mapping.
        ResolveControlFlowTargets(cfg, blueprint, nodeById, execConns);

        // ── Step 4: Build CFG edges ──
        BuildEdgesFromTopology(cfg, blueprint, nodeById, execConns);

        // ── Step 5: Set entry block ──
        cfg.EntryBlock = cfg.Blocks.FirstOrDefault(b => b.IsMainBlock) ?? cfg.Blocks.FirstOrDefault();

        // ── Step 6: Set block types ──
        ClassifyBlockTypes(cfg);

        // ── Step 7: Set parent loop references ──
        SetParentLoopReferences(cfg);

        // ── Step 8: Transfer PubVar data ──
        cfg.PubVarDeclarations = allPubVars;
        cfg.PubVarCounter = pubVarCounter;

        // ── Step 9: Transfer const declarations ──
        // Collect ConstNode (initialized variables) and VariableNode (uninitialized variables)
        foreach (var node in blueprint.Nodes.OfType<ConstNode>())
        {
            cfg.ConstDeclarations.Add(new ConstDeclaration
            {
                Name = node.ConstName,
                Type = node.ConstType,
                DefaultValue = node.ConstValue,
                InitialValueExpression = null  // Blueprint doesn't store source expression
            });
        }
        foreach (var node in blueprint.Nodes.OfType<VariableNode>())
        {
            cfg.ConstDeclarations.Add(new ConstDeclaration
            {
                Name = node.VarName,
                Type = node.VarType,
                DefaultValue = null,
                InitialValueExpression = null
            });
        }

        Log.Debug("[BP2CFGConverter] Built CFG: {BlockCount} blocks, {EdgeCount} edges",
            cfg.Blocks.Count, cfg.Blocks.Sum(b => b.Successors.Count));

        // ── Step 9: Populate debug node mapping ──
        if (cfg.DebugContext != null && cfg.Blocks != null)
        {
            foreach (var block in cfg.Blocks)
            {
                if (block?.Statements == null) continue;
                foreach (var stmt in block.Statements)
                {
                    if (stmt != null && !string.IsNullOrEmpty(stmt.StatementId))
                        cfg.DebugContext.StatementToNodeId[stmt.StatementId] = stmt.StatementId;
                }
            }
        }

        return cfg;
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 1: Connection Analysis
    // ════════════════════════════════════════════════════════════════════

    private static void AnalyzeConnections(
        Contract.Workflow.Blueprint blueprint,
        Dictionary<string, BlueprintNode> nodeById,
        List<BlueprintConnection> execConns,
        List<BlueprintConnection> dataConns,
        Dictionary<(string, string), DataEdgeInfo> inputDataMap,
        HashSet<(string, string)> consumedOutputs,
        List<string> allPubVars,
        List<string> autoPubVars,
        ref int pubVarCounter)
    {
        foreach (var node in blueprint.Nodes)
            nodeById[node.Id] = node;

        foreach (var conn in blueprint.Connections)
        {
            var sourceNode = blueprint.GetNodeById(conn.SourceNodeId);
            if (sourceNode == null) continue;

            var sourcePin = sourceNode.GetPinById(conn.SourcePinId);
            if (sourcePin == null) continue;

            if (sourcePin.Type == PinType.Execution)
            {
                execConns.Add(conn);
            }
            else
            {
                dataConns.Add(conn);

                var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
                if (targetNode != null)
                {
                    var targetPin = targetNode.GetPinById(conn.TargetPinId);
                    if (targetPin != null)
                    {
                        inputDataMap[(conn.TargetNodeId, targetPin.Name)] = new DataEdgeInfo
                        {
                            SourceNode = sourceNode,
                            SourcePinName = sourcePin.Name,
                            SourcePin = sourcePin,
                            PubVarName = conn.PubVarName,
                            Connection = conn
                        };

                        consumedOutputs.Add((conn.SourceNodeId, sourcePin.Name));
                    }
                }
            }
        }

        // Auto-assign PubVar names where needed
        foreach (var conn in dataConns)
        {
            if (!string.IsNullOrEmpty(conn.PubVarName)) continue;

            var sourceNode = blueprint.GetNodeById(conn.SourceNodeId);
            if (sourceNode == null) continue;
            if (sourceNode.NodeType == BlueprintNodeType.Const) continue;

            if (sourceNode.NodeType is BlueprintNodeType.Call
                or BlueprintNodeType.CallHelper
                || (sourceNode is BuiltinFunctionNode bfn && bfn.FunctionName == "Get"))
            {
                var sourcePin = sourceNode.GetPinById(conn.SourcePinId);
                if (sourcePin == null) continue;

                var pubVar = ExprUtils.GeneratePubVarName(pubVarCounter++);
                conn.PubVarName = pubVar;
                autoPubVars.Add(pubVar);

                var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
                if (targetNode != null)
                {
                    var targetPin = targetNode.GetPinById(conn.TargetPinId);
                    if (targetPin != null && inputDataMap.TryGetValue(
                        (conn.TargetNodeId, targetPin.Name), out var info))
                    {
                        info.PubVarName = pubVar;
                    }
                }
            }
        }

        foreach (var conn in dataConns)
        {
            if (!string.IsNullOrEmpty(conn.PubVarName) && !allPubVars.Contains(conn.PubVarName))
                allPubVars.Add(conn.PubVarName);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 2: Reachability Analysis
    // ════════════════════════════════════════════════════════════════════

    private static HashSet<string> FindReachableNodeIds(
        Contract.Workflow.Blueprint blueprint,
        Dictionary<string, BlueprintNode> nodeById,
        List<BlueprintConnection> execConns)
    {
        var reachable = new HashSet<string>();
        var entry = blueprint.Nodes.FirstOrDefault(n =>
            n.NodeType == BlueprintNodeType.Entry || n.NodeType == BlueprintNodeType.PluginTrigger);
        if (entry == null) return reachable;

        var queue = new Queue<BlueprintNode>();
        queue.Enqueue(entry);
        reachable.Add(entry.Id);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var pin in current.OutputPins)
            {
                if (pin.Type != PinType.Execution) continue;
                var conn = execConns.FirstOrDefault(c => c.SourcePinId == pin.Id);
                if (conn == null) continue;
                var target = blueprint.GetNodeById(conn.TargetNodeId);
                if (target == null || reachable.Contains(target.Id)) continue;
                reachable.Add(target.Id);
                queue.Enqueue(target);
            }
        }

        return reachable;
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 3a: Build Blocks from BlockScopes
    // ════════════════════════════════════════════════════════════════════

    private void BuildBlocksFromScopes(
        Contract.Workflow.Blueprint blueprint,
        ControlFlowGraph cfg,
        Dictionary<string, BlueprintNode> nodeById,
        HashSet<string> reachableNodeIds,
        List<BlueprintConnection> execConns)
    {
        foreach (var scope in blueprint.BlockScopes)
        {
            var block = new CFGBlock
            {
                Name = scope.Name,
                Type = scope.IsMainBlock ? CFGBlockType.Entry : CFGBlockType.Basic,
                NextBlockName = scope.NextBlockName,
            };

            foreach (var nodeId in scope.NodeIds)
            {
                if (!reachableNodeIds.Contains(nodeId)) continue;
                if (!nodeById.TryGetValue(nodeId, out var node)) continue;

                var stmt = GenerateStatement(node, nodeById, execConns, blueprint);
                if (stmt != null)
                    block.Statements.Add(stmt);
            }

            cfg.Blocks.Add(block);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 3b: Build Blocks from Topology (no BlockScopes)
    // ════════════════════════════════════════════════════════════════════

    private void BuildBlocksFromTopology(
        Contract.Workflow.Blueprint blueprint,
        ControlFlowGraph cfg,
        Dictionary<string, BlueprintNode> nodeById,
        HashSet<string> reachableNodeIds,
        List<BlueprintConnection> execConns)
    {
        var entryNode = blueprint.Nodes.FirstOrDefault(n =>
            n.NodeType == BlueprintNodeType.Entry || n.NodeType == BlueprintNodeType.PluginTrigger);
        if (entryNode == null) return;

        // DFS from Entry, splitting at Branch/Loop nodes
        var mainBlock = new CFGBlock { Name = MainBlock, Type = CFGBlockType.Entry };
        cfg.Blocks.Add(mainBlock);

        var visited = new HashSet<string>();
        var pendingControlFlowNodes = new List<BlueprintNode>();
        var loopNodes = new Dictionary<string, BlueprintNode>();
        var loopOwnerBlockNames = new Dictionary<string, string>();
        var blockCounter = 0;

        WalkNode(entryNode, mainBlock, cfg, nodeById, reachableNodeIds, execConns,
            blueprint, visited, pendingControlFlowNodes, loopNodes,
            loopOwnerBlockNames, ref blockCounter, loopbackTargetId: null);

        // Process sub-graphs (Branch/Loop bodies)
        ProcessSubGraphs(cfg, nodeById, reachableNodeIds, execConns, blueprint,
            pendingControlFlowNodes, loopNodes, loopOwnerBlockNames, ref blockCounter);
    }

    private void WalkNode(
        BlueprintNode node, CFGBlock currentBlock, ControlFlowGraph cfg,
        Dictionary<string, BlueprintNode> nodeById, HashSet<string> reachableNodeIds,
        List<BlueprintConnection> execConns, Contract.Workflow.Blueprint blueprint,
        HashSet<string> visited, List<BlueprintNode> pendingControlFlowNodes,
        Dictionary<string, BlueprintNode> loopNodes,
        Dictionary<string, string> loopOwnerBlockNames,
        ref int blockCounter, string? loopbackTargetId)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);

        if (loopbackTargetId != null && node.Id == loopbackTargetId)
        {
            var ownerName = loopOwnerBlockNames.TryGetValue(loopbackTargetId, out var n) ? n : null;
            currentBlock.Statements.Add(CreateToLoopCondStatement(ownerName));
            currentBlock.Successors.Add(new CFGEdge
            {
                FromBlockName = currentBlock.Name,
                ToBlockName = ownerName ?? loopbackTargetId,
                Type = CFGEdgeType.LoopbackToCondition
            });
            return;
        }

        var stmt = GenerateStatement(node, nodeById, execConns, blueprint);
        var isControlFlow = IsControlFlowNode(node);

        if (stmt != null)
        {
            currentBlock.Statements.Add(stmt);

            if (isControlFlow)
            {
                pendingControlFlowNodes.Add(node);
                if (stmt.Kind == CFGStatementKind.Loop)
                {
                    loopNodes[node.Id] = node;
                    loopOwnerBlockNames[node.Id] = currentBlock.Name;
                }
            }
        }

        // If ToLoopCond, don't follow exec chain
        if (stmt is { Kind: CFGStatementKind.ToLoopCond })
            return;

        if (isControlFlow)
        {
            pendingControlFlowNodes.Add(node);
            return;
        }

        // Follow execution chain
        var execOut = node.OutputPins.FirstOrDefault(p => p.Name == Exec);
        if (execOut == null) return;

        var execConn = execConns.FirstOrDefault(c => c.SourcePinId == execOut.Id);
        if (execConn == null) return;

        var nextNode = blueprint.GetNodeById(execConn.TargetNodeId);
        if (nextNode == null) return;

        if (loopbackTargetId != null && nextNode.Id == loopbackTargetId)
        {
            var ownerName2 = loopOwnerBlockNames.TryGetValue(loopbackTargetId, out var n2) ? n2 : null;
            currentBlock.Statements.Add(CreateToLoopCondStatement(ownerName2));
            currentBlock.Successors.Add(new CFGEdge
            {
                FromBlockName = currentBlock.Name,
                ToBlockName = ownerName2 ?? loopbackTargetId,
                Type = CFGEdgeType.LoopbackToCondition
            });
            return;
        }

        WalkNode(nextNode, currentBlock, cfg, nodeById, reachableNodeIds, execConns,
            blueprint, visited, pendingControlFlowNodes, loopNodes,
            loopOwnerBlockNames, ref blockCounter, loopbackTargetId);
    }

    private void ProcessSubGraphs(
        ControlFlowGraph cfg, Dictionary<string, BlueprintNode> nodeById,
        HashSet<string> reachableNodeIds, List<BlueprintConnection> execConns,
        Contract.Workflow.Blueprint blueprint,
        List<BlueprintNode> pendingControlFlowNodes,
        Dictionary<string, BlueprintNode> loopNodes,
        Dictionary<string, string> loopOwnerBlockNames,
        ref int blockCounter)
    {
        var processedNodes = new HashSet<string>();

        while (pendingControlFlowNodes.Count > 0)
        {
            var pendingList = pendingControlFlowNodes.ToList();
            pendingControlFlowNodes.Clear();

            foreach (var node in pendingList)
            {
                if (processedNodes.Contains(node.Id)) continue;
                processedNodes.Add(node.Id);

                ProcessControlFlowSubGraph(node, cfg, nodeById, reachableNodeIds, execConns,
                    blueprint, loopNodes, loopOwnerBlockNames, ref blockCounter);
            }
        }
    }

    private void ProcessControlFlowSubGraph(
        BlueprintNode cfNode, ControlFlowGraph cfg,
        Dictionary<string, BlueprintNode> nodeById, HashSet<string> reachableNodeIds,
        List<BlueprintConnection> execConns, Contract.Workflow.Blueprint blueprint,
        Dictionary<string, BlueprintNode> loopNodes,
        Dictionary<string, string> loopOwnerBlockNames, ref int blockCounter)
    {
        // Get output arms from the export strategy
        IEnumerable<OutputArmDescriptor>? arms = null;

        if (cfNode is BuiltinFunctionNode bfn
            && _builtinFunctionStrategies.TryGetValue(bfn.FunctionName, out var bfStrat))
        {
            arms = bfStrat.GetOutputArms(cfNode);
        }
        else if (_strategies.TryGetValue(cfNode.NodeType, out var strat))
        {
            arms = strat.GetOutputArms(cfNode);
        }

        if (arms == null) return;

        foreach (var arm in arms)
        {
            var pin = cfNode.OutputPins.FirstOrDefault(p => p.Name == arm.PinName);
            if (pin == null) continue;

            var conn = execConns.FirstOrDefault(c => c.SourcePinId == pin.Id);
            if (conn == null) continue;

            var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
            if (targetNode == null) continue;

            var blockName = $"Block_{blockCounter++}";
            var block = new CFGBlock
            {
                Name = blockName,
                Type = arm.IsLoopback ? CFGBlockType.LoopBody : CFGBlockType.Basic,
                ParentLoopBlockName = arm.IsLoopback ? FindContainingBlockName(cfg, cfNode.Id) : null
            };
            cfg.Blocks.Add(block);

            if (arm.IsLoopback)
                loopOwnerBlockNames[cfNode.Id] = blockName;

            WalkNode(targetNode, block, cfg, nodeById, reachableNodeIds, execConns,
                blueprint, new HashSet<string>(), pendingControlFlowNodes: new(),
                loopNodes, loopOwnerBlockNames, ref blockCounter,
                loopbackTargetId: arm.IsLoopback ? cfNode.Id : null);

            // Update the CFG statement with the resolved target block name
            var cfStmt = FindBranchStatement(cfg, cfNode.Id);
            if (cfStmt != null)
            {
                // Map arm pin name to TrueBlockName/FalseBlockName
                // First non-loopback arm → TrueBlockName, second → FalseBlockName
                var normalArms = arms.Where(a => !a.IsLoopback).ToList();
                var armIndex = normalArms.IndexOf(arm);
                if (armIndex == 0)
                    cfStmt.TrueBlockName = blockName;
                else if (armIndex == 1)
                    cfStmt.FalseBlockName = blockName;

                cfStmt.OriginalExpression = RegenerateBranchSource(cfStmt);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 4: Build CFG Edges from Blueprint Topology
    // ════════════════════════════════════════════════════════════════════

    private void BuildEdgesFromTopology(
        ControlFlowGraph cfg, Contract.Workflow.Blueprint blueprint,
        Dictionary<string, BlueprintNode> nodeById, List<BlueprintConnection> execConns)
    {
        foreach (var block in cfg.Blocks)
        {
            if (block.Successors.Count > 0) continue;
            if (block.EndsWithControlFlow) continue;
            if (block.Statements.Count == 0) continue;

            var lastStmt = block.Statements[^1];

            // Use registry to determine edge types for control flow statements
            if (!string.IsNullOrEmpty(lastStmt.FunctionName)
                && _builtinFunctionStrategies.TryGetValue(lastStmt.FunctionName, out var builtinStrat)
                && builtinStrat.IsControlFlow)
            {
                var arms = builtinStrat.GetOutputArms(null!);
                foreach (var arm in arms)
                {
                    var targetBlock = arm.IsLoopback ? lastStmt.ToLoopCondReturnTo
                        : (IsFirstNormalArm(arms, arm) ? lastStmt.TrueBlockName : lastStmt.FalseBlockName);

                    if (string.IsNullOrEmpty(targetBlock)) continue;

                    block.Successors.Add(new CFGEdge
                    {
                        FromBlockName = block.Name,
                        ToBlockName = targetBlock,
                        Type = GetEdgeType(arm),
                        PinName = arm.PinName
                    });
                }
                continue;
            }

            // Non-registry control flow handling (fallback)
            if (lastStmt.Kind == CFGStatementKind.ToLoopCond)
            {
                if (!string.IsNullOrEmpty(lastStmt.ToLoopCondReturnTo))
                    block.Successors.Add(new CFGEdge
                    {
                        FromBlockName = block.Name,
                        ToBlockName = lastStmt.ToLoopCondReturnTo,
                        Type = CFGEdgeType.LoopbackToCondition
                    });
                continue;
            }

            if (lastStmt.Kind == CFGStatementKind.Break)
            {
                block.Successors.Add(new CFGEdge
                {
                    FromBlockName = block.Name,
                    ToBlockName = "__break__",
                    Type = CFGEdgeType.Break
                });
                continue;
            }

            if (lastStmt.Kind == CFGStatementKind.ToLoopCond)
            {
                if (!string.IsNullOrEmpty(lastStmt.ToLoopCondReturnTo))
                    block.Successors.Add(new CFGEdge
                    {
                        FromBlockName = block.Name,
                        ToBlockName = lastStmt.ToLoopCondReturnTo,
                        Type = CFGEdgeType.LoopbackToCondition
                    });
                continue;
            }

            if (lastStmt.Kind == CFGStatementKind.Break)
            {
                block.Successors.Add(new CFGEdge
                {
                    FromBlockName = block.Name,
                    ToBlockName = "__break__",
                    Type = CFGEdgeType.Break
                });
                continue;
            }

            // Sequential fall-through: derive NextBlockName from the block or from exec connections
            if (!string.IsNullOrEmpty(block.NextBlockName))
            {
                block.Successors.Add(new CFGEdge
                {
                    FromBlockName = block.Name,
                    ToBlockName = block.NextBlockName,
                    Type = CFGEdgeType.Sequential,
                    PinName = Exec
                });
            }
        }

        // For BlockScopes-based blocks that don't have NextBlockName and don't end with control flow,
        // resolve NextBlockName from execution connections
        ResolveMissingNextBlockNames(cfg, blueprint, nodeById, execConns);
    }

    /// <summary>
    /// Resolves missing NextBlockName for blocks that don't end with control flow
    /// by following exec connections from the scope's last reachable node.
    /// This replaces the ad-hoc NextBlockName fallback logic.
    /// </summary>
    private static void ResolveMissingNextBlockNames(
        ControlFlowGraph cfg, Contract.Workflow.Blueprint blueprint,
        Dictionary<string, BlueprintNode> nodeById, List<BlueprintConnection> execConns)
    {
        foreach (var block in cfg.Blocks)
        {
            if (!string.IsNullOrEmpty(block.NextBlockName)) continue;
            if (block.EndsWithControlFlow) continue;
            if (block.Successors.Count > 0) continue;  // Already has edges

            // Find the scope containing this block's nodes
            var scope = blueprint.BlockScopes.FirstOrDefault(s => s.Name == block.Name);
            if (scope == null) continue;

            // Find the last reachable node in this scope with an exec output
            string? lastNodeId = null;
            for (int i = scope.NodeIds.Count - 1; i >= 0; i--)
            {
                if (nodeById.ContainsKey(scope.NodeIds[i]))
                {
                    lastNodeId = scope.NodeIds[i];
                    break;
                }
            }
            if (lastNodeId == null) continue;
            if (!nodeById.TryGetValue(lastNodeId, out var lastNode)) continue;

            var execOut = lastNode.OutputPins.FirstOrDefault(p => p.Name == Exec);
            if (execOut == null) continue;

            var execConn = execConns.FirstOrDefault(c => c.SourcePinId == execOut.Id);
            if (execConn == null) continue;

            var targetNode = blueprint.GetNodeById(execConn.TargetNodeId);
            if (targetNode == null) continue;

            // Find which CFG block contains the target node
            var targetBlockName = FindBlockContainingNode(cfg, targetNode.Id, blueprint);
            if (targetBlockName != null)
            {
                block.NextBlockName = targetBlockName;
                block.Successors.Add(new CFGEdge
                {
                    FromBlockName = block.Name,
                    ToBlockName = targetBlockName,
                    Type = CFGEdgeType.Sequential,
                    PinName = Exec
                });
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 6: Block Type Classification
    // ════════════════════════════════════════════════════════════════════

    private static void ClassifyBlockTypes(ControlFlowGraph cfg)
    {
        foreach (var block in cfg.Blocks)
        {
            if (block.IsMainBlock) continue;  // Already classified as Entry

            if (block.Statements.Count > 0)
            {
                var lastStmt = block.Statements[^1];
                block.Type = lastStmt.Kind switch
                {
                    CFGStatementKind.Branch => CFGBlockType.BranchHeader,
                    CFGStatementKind.Loop => CFGBlockType.LoopHeader,
                    _ => block.Type
                };
            }

            // Classify blocks that are targets of LoopBody edges
            if (block.ParentLoopBlockName != null && block.Type == CFGBlockType.Basic)
                block.Type = CFGBlockType.LoopBody;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 7: Set Parent Loop References
    // ════════════════════════════════════════════════════════════════════

    private static void SetParentLoopReferences(ControlFlowGraph cfg)
    {
        // For each block with a LoopbackToCondition edge, set ParentLoopBlockName
        foreach (var block in cfg.Blocks)
        {
            if (block.ParentLoopBlockName != null) continue;

            var toLoopCondEdge = block.Successors.FirstOrDefault(e => e.Type == CFGEdgeType.LoopbackToCondition);
            if (toLoopCondEdge != null)
                block.ParentLoopBlockName = toLoopCondEdge.ToBlockName;
        }

        // For each block that is the target of a LoopBody edge, set ParentLoopBlockName
        foreach (var block in cfg.Blocks)
        {
            if (block.ParentLoopBlockName != null) continue;

            foreach (var other in cfg.Blocks)
            {
                var loopBodyEdge = other.Successors.FirstOrDefault(e =>
                    e.Type == CFGEdgeType.LoopBody && e.ToBlockName == block.Name);
                if (loopBodyEdge != null)
                {
                    block.ParentLoopBlockName = other.Name;
                    block.Type = CFGBlockType.LoopBody;
                    break;
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Statement Generation
    // ════════════════════════════════════════════════════════════════════

    private CFGStatement? GenerateStatement(
        BlueprintNode node, Dictionary<string, BlueprintNode> nodeById,
        List<BlueprintConnection> execConns, Contract.Workflow.Blueprint blueprint)
    {
        // Delegate to existing strategy-based generation, then convert to CFGStatement
        var blockStmt = GenerateBlockStatement(node, nodeById, execConns, blueprint);
        if (blockStmt == null) return null;

        return ConvertBlockStatementToCfgStatement(blockStmt, node);
    }

    /// <summary>
    /// Generates a BlockStatement using the existing export strategy system.
    /// Reuses the proven strategy-based statement generation logic.
    /// </summary>
    private BlockStatement? GenerateBlockStatement(
        BlueprintNode node, Dictionary<string, BlueprintNode> nodeById,
        List<BlueprintConnection> execConns, Contract.Workflow.Blueprint blueprint)
    {
        switch (node.NodeType)
        {
            case BlueprintNodeType.Entry:
            case BlueprintNodeType.PluginTrigger:
                return null;
            case BlueprintNodeType.Call:
            {
                if (node is not CallNode call) return null;
                var callArgs = _exportHelper.GetInputArgs(call);
                string sourceCode;
                if (!string.IsNullOrEmpty(call.TargetDevice))
                {
                    // 跨设备调用：PluginCallWithTarget("plugin", "method", "device", args...)
                    var pluginNameLit = $"\"{call.PluginName}\"";
                    var methodNameLit = $"\"{call.FunctionName}\"";
                    var targetDeviceLit = $"\"{call.TargetDevice}\"";
                    // Include ExtraArguments (stored by ConfigureNode from PluginCallWithTarget's extra params)
                    var extraArgs = call.ExtraArguments.Count > 0
                        ? ", " + string.Join(", ", call.ExtraArguments.Select(
                            arg => NodeExportHelper.FormatLiteralValue(arg, _currentCtx)))
                        : "";
                    sourceCode = $"PluginCallWithTarget({pluginNameLit}, {methodNameLit}, {targetDeviceLit}{extraArgs})";
                }
                else
                {
                    // 本地调用：PluginName.MethodName(args...)
                    var funcRef = string.IsNullOrEmpty(call.PluginName)
                        ? call.FunctionName : $"{call.PluginName}.{call.FunctionName}";
                    sourceCode = $"{funcRef}({callArgs})";
                }
                var callStmt = new ExpressionStatement
                {
                    Expression = sourceCode,
                    SourceCode = sourceCode + ";",
                    LineNumber = 1
                };
                PostProcessCallReturn(node, callStmt);
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
                PostProcessCallReturn(node, helperStmt);
                return helperStmt;
            }
            case BlueprintNodeType.BuiltinFunction:
            {
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
            Log.Warning("[BP2CFGConverter] Unhandled node type: {NodeType}", node.NodeType);
            return null;
        }

        var stmt = strategy.ToStatement(node, _exportHelper);
        if (stmt == null) return null;

        PostProcessCallReturn(node, stmt);
        return stmt;
    }

    private void PostProcessCallReturn(BlueprintNode node, BlockStatement stmt)
    {
        if (_currentCtx == null) return;

        if (node.NodeType is BlueprintNodeType.Call or BlueprintNodeType.CallHelper
            && stmt is ExpressionStatement exprStmt)
        {
            var returnPin = node.OutputPins.FirstOrDefault(p => p.Name == Return);
            bool hasReturn = returnPin != null && _currentCtx.ConsumedOutputs.Contains((node.Id, Return));
            if (hasReturn)
            {
                var pubVar = NodeExportHelper.FindOutputPubVar(node, Return, _currentCtx);
                if (pubVar != null)
                    exprStmt.SourceCode = $"{pubVar} = {exprStmt.Expression};";
            }
        }
    }

    private ConversionContext? _currentCtx;

    /// <summary>
    /// Sets the context for PubVar resolution during statement generation.
    /// Called before Build() when BlockScopes path is used.
    /// </summary>
    public void SetContext(Contract.Workflow.Blueprint blueprint, ConversionContext? ctx)
    {
        _exportHelper.SetContext(blueprint, ctx);
        _currentCtx = ctx;
    }

    // ════════════════════════════════════════════════════════════════════
    // Statement Conversion Helpers
    // ════════════════════════════════════════════════════════════════════

    private CFGStatement ConvertBlockStatementToCfgStatement(BlockStatement blockStmt, BlueprintNode node)
    {
        var cfgStmt = new CFGStatement
        {
            StatementId = node.Id,  // Use node ID as statement ID for node mapping
            OriginalExpression = blockStmt.SourceCode,
            SourceLine = blockStmt.LineNumber,
        };

        switch (blockStmt)
        {
            case FlowControlStatement flow:
                cfgStmt.Kind = flow.ControlType switch
                {
                    FlowControlType.Branch => CFGStatementKind.Branch,
                    FlowControlType.Loop => CFGStatementKind.Loop,
                    FlowControlType.ToLoopCond => CFGStatementKind.ToLoopCond,
                    FlowControlType.Break => CFGStatementKind.Break,
                    _ => CFGStatementKind.Unknown
                };
                cfgStmt.FunctionName = flow.ControlType switch
                {
                    FlowControlType.Branch => "Branch",
                    FlowControlType.Loop => "Loop",
                    FlowControlType.ToLoopCond => "ToLoopCond",
                    FlowControlType.Break => "Break",
                    _ => null
                };
                cfgStmt.ConditionExpression = flow.ConditionExpression;
                cfgStmt.TrueBlockName = flow.TrueBlockName;
                cfgStmt.FalseBlockName = flow.FalseBlockName;
                cfgStmt.ToLoopCondReturnTo = flow.ToLoopCondReturnTo;
                cfgStmt.ConditionPubVar = flow.ConditionExpression?.Trim();
                break;

            case ExpressionStatement expr:
                cfgStmt.OriginalExpression = expr.SourceCode;

                // Kind & FunctionName from strategy (built-in function metadata)
                if (node is BuiltinFunctionNode bfn
                    && _builtinFunctionStrategies.TryGetValue(bfn.FunctionName, out var bfStrat)
                    && bfStrat is BuiltinFunctionExportStrategyAdapter bfAdapter)
                {
                    cfgStmt.Kind = bfAdapter.StatementKind;
                    cfgStmt.FunctionName = bfAdapter.FunctionName;
                }
                else if (_strategies.TryGetValue(node.NodeType, out var strat)
                    && strat is BuiltinFunctionExportStrategyAdapter adapter)
                {
                    cfgStmt.Kind = adapter.StatementKind;
                    cfgStmt.FunctionName = adapter.FunctionName;
                }
                else
                {
                    cfgStmt.Kind = CFGStatementKind.Expression;
                    // Fallback: parse FunctionName from SourceCode for nodes without strategy (CallHelper, Call, etc.)
                    var fallbackParsed = ExprUtils.ParseStatement(expr.SourceCode);
                    if (fallbackParsed?.rightExpr is InvocationExpressionSyntax fallbackInvoke)
                        cfgStmt.FunctionName = ExprUtils.GetMethodName(fallbackInvoke);
                }

                // Arguments from node input pins (no text parsing)
                cfgStmt.Arguments = new List<string>();
                foreach (var pin in node.InputPins)
                {
                    if (pin.Name != "Exec")
                        cfgStmt.Arguments.Add(_exportHelper.GetInputValue(node, pin.Name));
                }

                // SetVarName / GetVarName from node input pins for Set/Get
                if (node is BuiltinFunctionNode bfnProps)
                {
                    if (bfnProps.FunctionName == "Set")
                    {
                        var varPin = node.InputPins.FirstOrDefault(p => p.Name == "VarName");
                        if (varPin != null && !string.IsNullOrEmpty(varPin.DefaultValue))
                            cfgStmt.SetVarName = varPin.DefaultValue;
                    }
                    else if (bfnProps.FunctionName == "Get")
                    {
                        var varPin = node.InputPins.FirstOrDefault(p => p.Name == "VarName");
                        if (varPin != null && !string.IsNullOrEmpty(varPin.DefaultValue))
                            cfgStmt.GetVarName = varPin.DefaultValue;
                    }
                }

                // PubVarTarget from consumed output lookup
                if (_currentCtx != null)
                {
                    var outputPin = node.OutputPins.FirstOrDefault(p => p.Type != PinType.Execution);
                    if (outputPin != null)
                        cfgStmt.PubVarTarget = NodeExportHelper.FindOutputPubVar(node, outputPin.Name, _currentCtx);
                }

                if (node is CallNode callNode)
                    cfgStmt.FullFunctionName = string.IsNullOrEmpty(callNode.PluginName)
                        ? callNode.FunctionName
                        : $"{callNode.PluginName}.{callNode.FunctionName}";
                break;

            default:
                cfgStmt.Kind = CFGStatementKind.Unknown;
                break;
        }

        return cfgStmt;
    }

    private static CFGStatement CreateToLoopCondStatement(string? returnTo)
    {
        var stmt = new CFGStatement
        {
            Kind = CFGStatementKind.ToLoopCond,
            ToLoopCondReturnTo = returnTo,
            OriginalExpression = returnTo != null
                ? $"NextBlock = ToLoopCond(\"{returnTo}\");"
                : "NextBlock = ToLoopCond();",
            SourceLine = 1
        };
        return stmt;
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 3.5: Resolve Branch/Loop Target Block Names
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After blocks are built, resolves the TrueBlockName/FalseBlockName of
    /// Branch and Loop statements from Blueprint execution connections.
    /// Strategy-generated statements don't know their target block names;
    /// we derive them from the node's output pins → connections → target node → containing block.
    /// </summary>
    private static void ResolveControlFlowTargets(
        ControlFlowGraph cfg,
        Contract.Workflow.Blueprint blueprint,
        Dictionary<string, BlueprintNode> nodeById,
        List<BlueprintConnection> execConns)
    {
        foreach (var block in cfg.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt.Kind is not (CFGStatementKind.Branch or CFGStatementKind.Loop))
                    continue;
                if (!string.IsNullOrEmpty(stmt.TrueBlockName) && !string.IsNullOrEmpty(stmt.FalseBlockName))
                    continue;

                if (!nodeById.TryGetValue(stmt.StatementId, out var node))
                    continue;

                ResolveControlFlowTargetsForNode(stmt, node, cfg, blueprint, execConns);
            }
        }
    }

    private static void ResolveControlFlowTargetsForNode(
        CFGStatement stmt, BlueprintNode cfNode,
        ControlFlowGraph cfg, Contract.Workflow.Blueprint blueprint,
        List<BlueprintConnection> execConns)
    {
        foreach (var pin in cfNode.OutputPins.Where(p => p.Type == PinType.Execution))
        {
            var conn = execConns.FirstOrDefault(c => c.SourcePinId == pin.Id);
            if (conn == null) continue;

            var targetBlock = FindBlockContainingNode(cfg, conn.TargetNodeId, blueprint);
            if (targetBlock == null) continue;

            // Assign first unset target
            if (string.IsNullOrEmpty(stmt.TrueBlockName))
                stmt.TrueBlockName = targetBlock;
            else if (string.IsNullOrEmpty(stmt.FalseBlockName))
                stmt.FalseBlockName = targetBlock;
        }

        if (!string.IsNullOrEmpty(stmt.TrueBlockName) || !string.IsNullOrEmpty(stmt.FalseBlockName))
            stmt.OriginalExpression = RegenerateBranchSource(stmt);
    }

    // ════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ════════════════════════════════════════════════════════════════════

    private static CFGStatement? FindBranchStatement(ControlFlowGraph cfg, string nodeId)
    {
        foreach (var block in cfg.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt.StatementId == nodeId)
                    return stmt;
            }
        }
        return null;
    }

    private static string? FindContainingBlockName(ControlFlowGraph cfg, string nodeId)
    {
        foreach (var block in cfg.Blocks)
        {
            if (block.Statements.Any(s => s.StatementId == nodeId))
                return block.Name;
        }
        return null;
    }

    private static string? FindBlockContainingNode(ControlFlowGraph cfg, string targetNodeId,
        Contract.Workflow.Blueprint blueprint)
    {
        // Find which BlockScope contains the target node
        foreach (var scope in blueprint.BlockScopes)
        {
            if (scope.NodeIds.Contains(targetNodeId))
                return scope.Name;
        }

        // Fallback: find which CFG block contains a statement with this node ID
        foreach (var block in cfg.Blocks)
        {
            if (block.Statements.Any(s => s.StatementId == targetNodeId))
                return block.Name;
        }

        return null;
    }

    private static string RegenerateBranchSource(CFGStatement branchStmt)
    {
        return branchStmt.Kind == CFGStatementKind.Loop
            ? $"NextBlock = Loop({branchStmt.ConditionExpression}, \"{branchStmt.TrueBlockName}\", \"{branchStmt.FalseBlockName}\");"
            : $"NextBlock = Branch({branchStmt.ConditionExpression}, \"{branchStmt.TrueBlockName}\", \"{branchStmt.FalseBlockName}\");";
    }

    private static bool IsFirstNormalArm(IEnumerable<OutputArmDescriptor> arms, OutputArmDescriptor target)
    {
        var normalArms = arms.Where(a => !a.IsLoopback).ToList();
        return normalArms.Count > 0 && normalArms[0].PinName == target.PinName;
    }

    private static CFGEdgeType GetEdgeType(OutputArmDescriptor arm)
    {
        if (arm.IsLoopback) return CFGEdgeType.LoopbackToCondition;
        // For normal arms: first → BranchTrue/LoopBody, second → BranchFalse/LoopEnd
        // This is a simplified heuristic; more sophisticated logic can be added if needed
        return CFGEdgeType.BranchTrue;
    }

    // ─── Node Type Helpers ────────────────────────────────────────────

    private bool IsControlFlowNode(BlueprintNode node) =>
        node is BuiltinFunctionNode bfn
        && _builtinFunctionStrategies.TryGetValue(bfn.FunctionName, out var strat)
        && strat.IsControlFlow;

    private bool IsBranchNode(BlueprintNode node) =>
        node is BuiltinFunctionNode bfn && bfn.FunctionName == "Branch";

    private bool IsLoopNode(BlueprintNode node) =>
        node is BuiltinFunctionNode bfn && bfn.FunctionName == "Loop";
}
