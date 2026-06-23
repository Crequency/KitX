using KitX.Core.Contract.Workflow;
using KitX.Workflow.Blueprint;
using Serilog;

using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Blocks;

using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Builds a <see cref="ControlFlowGraph"/> from a <see cref="Blueprint"/>.
/// This is the unified BP→BS algorithm.
///
/// Whether <see cref="KitX.Core.Contract.Workflow.Blueprint.BlockScopes"/> is present or not,
/// this single algorithm produces a CFG. When BlockScopes are available, they guide
/// block membership; when absent, blocks are derived from topology alone.
/// </summary>
internal class BP2CFGConverter
{
    private readonly Dictionary<string, IBuiltinFunctionDefinition> _builtinFunctionStrategies;
    private readonly NodeExportHelper _exportHelper;

    public BP2CFGConverter(
        Dictionary<string, IBuiltinFunctionDefinition> builtinFunctionStrategies,
        NodeExportHelper exportHelper)
    {
        _builtinFunctionStrategies = builtinFunctionStrategies;
        _exportHelper = exportHelper;
    }

    /// <summary>
    /// Builds a ControlFlowGraph from a Blueprint.
    /// Single entry point that unifies the BlockScopes-based and topology-based paths.
    /// </summary>
    public ControlFlowGraph Build(KitX.Core.Contract.Workflow.Blueprint blueprint)
    {
        var cfg = new ControlFlowGraph
        {
            DebugStatementToNodeId = new Dictionary<string, string>()
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
        // v5.0: blueprint.PubVarNames carries user-declared PubVars that may not appear on any
        // data connection (e.g. a PubVar only written via the implicit-set pipeline form, where
        // the write materialises as a node output → PubVarTarget without a named connection).
        // Merge them in so the BS↔BP round-trip preserves user variable declarations.
        foreach (var name in blueprint.PubVarNames)
            if (!allPubVars.Contains(name))
                allPubVars.Add(name);
        cfg.PubVarDeclarations = allPubVars;
        cfg.PubVarCounter = pubVarCounter;

        // ── Step 9: Transfer const declarations ──
        // Collect ConstNode (initialized variables) and floating VariableNodes (uninitialized
        // declarations from ConstBlock). v5.0: write-site VariableNodes (those with Exec pins,
        // added by CFG2BPConverter when generating `Expr > var`) are excluded — they are
        // statement-level writes emitted by GenerateBlockStatement, not ConstBlock declarations.
        // Including them would duplicate the variable name in ConstBlock and break round-trip
        // (and risk CS0128 in CFG2CS). Detection by Exec pin is robust for literal writes too,
        // where no incoming data connection exists (DefaultValue is set instead).

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
        var seenVarNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in blueprint.Nodes.OfType<VariableNode>())
        {
            // Write-site VariableNodes carry Exec pins (added in CFG2BPConverter); skip them.
            if (node.InputPins.Any(p => p.Type == PinType.Execution)) continue;
            if (!seenVarNames.Add(node.VarName)) continue;  // dedup floating declarations
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
        if (cfg.DebugStatementToNodeId != null && cfg.Blocks != null)
        {
            foreach (var block in cfg.Blocks)
            {
                if (block?.Statements == null) continue;
                foreach (var stmt in block.Statements)
                {
                    if (stmt != null && !string.IsNullOrEmpty(stmt.StatementId))
                        cfg.DebugStatementToNodeId[stmt.StatementId] = stmt.StatementId;
                }
            }
        }

        return cfg;
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 1: Connection Analysis
    // ════════════════════════════════════════════════════════════════════

    private void AnalyzeConnections(
        KitX.Core.Contract.Workflow.Blueprint blueprint,
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

            // Value sources needing an auto PubVar: plugin/helper calls, or a builtin whose
            // descriptor declares AutoSynthesizePubVar (Get). Descriptor-driven — no name hardcoding.
            if (sourceNode.NodeType is BlueprintNodeType.Call
                or BlueprintNodeType.CallHelper
                || (sourceNode is BuiltinFunctionNode bfn
                    && _builtinFunctionStrategies.TryGetValue(bfn.FunctionName, out var def)
                    && def.AutoSynthesizePubVar))
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
        KitX.Core.Contract.Workflow.Blueprint blueprint,
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
        KitX.Core.Contract.Workflow.Blueprint blueprint,
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
        KitX.Core.Contract.Workflow.Blueprint blueprint,
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
        List<BlueprintConnection> execConns, KitX.Core.Contract.Workflow.Blueprint blueprint,
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
                Type = CFGEdgeType.Sequential  // v5.0: Goto-style back-edge uses Sequential
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
                // v5.0: ForLoop (IterativeCounted) registers like v4.0 Loop (IterativeJump) did,
                // so loopback resolution keeps working for ForLoop-bodied blueprints.
                if (stmt.FlowControlShape == FlowControlType.IterativeCounted)
                {
                    loopNodes[node.Id] = node;
                    loopOwnerBlockNames[node.Id] = currentBlock.Name;
                }
            }
        }

        // v5.0: Goto (UnconditionalJump) ends the exec chain like v4.0 ToLoopCond (LoopBackedge).
        if (stmt is { FlowControlShape: FlowControlType.UnconditionalJump })
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
                Type = CFGEdgeType.Sequential  // v5.0: Goto-style back-edge uses Sequential
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
        KitX.Core.Contract.Workflow.Blueprint blueprint,
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
                    blueprint, pendingControlFlowNodes, loopNodes, loopOwnerBlockNames, ref blockCounter);
            }
        }
    }

    private void ProcessControlFlowSubGraph(
        BlueprintNode cfNode, ControlFlowGraph cfg,
        Dictionary<string, BlueprintNode> nodeById, HashSet<string> reachableNodeIds,
        List<BlueprintConnection> execConns, KitX.Core.Contract.Workflow.Blueprint blueprint,
        List<BlueprintNode> pendingControlFlowNodes,
        Dictionary<string, BlueprintNode> loopNodes,
        Dictionary<string, string> loopOwnerBlockNames, ref int blockCounter)
    {
        // Get output arms from the export strategy
        IEnumerable<OutputArmDescriptor>? arms = null;

        if (cfNode is BuiltinFunctionNode bfn
            && _builtinFunctionStrategies.TryGetValue(bfn.FunctionName, out var bfStrat))
        {
            arms = bfStrat.GetOutputArms();
        }

        if (arms == null) return;

        // For variadic output nodes (e.g. Switch), the descriptor's declared arms are a fixed
        // base set, but the node's actual output execution pins may be more. Iterate the real
        // pins and key each arm by pin name so N arms survive without positional loss.
        var loopbackPinNames = arms.Where(a => a.IsLoopback).Select(a => a.PinName).ToHashSet();

        foreach (var pin in cfNode.OutputPins.Where(p => p.Type == PinType.Execution))
        {
            var conn = execConns.FirstOrDefault(c => c.SourcePinId == pin.Id);
            if (conn == null) continue;

            var targetNode = blueprint.GetNodeById(conn.TargetNodeId);
            if (targetNode == null) continue;

            var isLoopback = loopbackPinNames.Contains(pin.Name);
            var blockName = $"Block_{blockCounter++}";
            var block = new CFGBlock
            {
                Name = blockName,
                // v5.0: loop bodies are plain Basic blocks (ForLoop re-entry via Goto).
                Type = CFGBlockType.Basic,
            };
            cfg.Blocks.Add(block);

            if (isLoopback)
                loopOwnerBlockNames[cfNode.Id] = blockName;

            // Propagate the shared pending list so nested control-flow nodes discovered during
            // sub-graph walking are queued for ProcessSubGraphs' outer loop. Previously this
            // passed `new()`, dropping nested control-flow nodes (e.g. a Loop inside a Branch's
            // True arm) on the floor — their blocks/arms were never built on the topology path.
            WalkNode(targetNode, block, cfg, nodeById, reachableNodeIds, execConns,
                blueprint, new HashSet<string>(), pendingControlFlowNodes,
                loopNodes, loopOwnerBlockNames, ref blockCounter,
                loopbackTargetId: isLoopback ? cfNode.Id : null);

            // Record the resolved target as an arm keyed by the output pin name.
            // This generalises the former positional True/False assignment so that
            // Branch(True/False), Loop(LoopBody/LoopEnd) and Switch(Default/0/1/...)
            // all preserve their identity.
            var cfStmt = FindBranchStatement(cfg, cfNode.Id);
            if (cfStmt != null)
            {
                var existing = cfStmt.Arms.FirstOrDefault(a => a.PinName == pin.Name);
                if (existing != null)
                    existing.TargetBlockName = blockName;
                else
                    cfStmt.Arms.Add(new BranchArm
                    {
                        PinName = pin.Name,
                        TargetBlockName = blockName,
                        IsLoopback = isLoopback
                    });

                cfStmt.OriginalExpression = RegenerateBranchSource(cfStmt);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 4: Build CFG Edges from Blueprint Topology
    // ════════════════════════════════════════════════════════════════════

    private void BuildEdgesFromTopology(
        ControlFlowGraph cfg, KitX.Core.Contract.Workflow.Blueprint blueprint,
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
                && builtinStrat.FlowControlShape != null)
            {
                // Edges are derived directly from the statement's resolved Arms, so N-way
                // Switch (Default/0/1/...) and variadic shapes survive without positional loss.
                foreach (var arm in lastStmt.Arms)
                {
                    if (string.IsNullOrEmpty(arm.TargetBlockName)) continue;

                    block.Successors.Add(new CFGEdge
                    {
                        FromBlockName = block.Name,
                        ToBlockName = arm.TargetBlockName,
                        Type = GetEdgeType(lastStmt.FlowControlShape, arm),
                        PinName = arm.PinName
                    });
                }
                continue;
            }

            // v5.0: Goto (UnconditionalJump) uses a Sequential edge to its target.
            if (lastStmt.FlowControlShape == FlowControlType.UnconditionalJump)
            {
                if (!string.IsNullOrEmpty(lastStmt.TrueBlockName))
                    block.Successors.Add(new CFGEdge
                    {
                        FromBlockName = block.Name,
                        ToBlockName = lastStmt.TrueBlockName,
                        Type = CFGEdgeType.Sequential
                    });
                continue;
            }

            if (lastStmt.FlowControlShape == FlowControlType.LoopExit)
            {
                block.Successors.Add(new CFGEdge
                {
                    FromBlockName = block.Name,
                    ToBlockName = "__break__",
                    Type = CFGEdgeType.Break
                });
                continue;
            }

            // Sequential fall-through: derive the target from the block's BlueprintBlockScope
            // (for the BlockScopes path) — the single source of truth is now the Sequential edge
            // built here, not a parallel CFGBlock.NextBlockName field.
            var scope = blueprint.BlockScopes.FirstOrDefault(s => s.Name == block.Name);
            var fallThrough = scope?.NextBlockName;
            if (!string.IsNullOrEmpty(fallThrough))
            {
                block.Successors.Add(new CFGEdge
                {
                    FromBlockName = block.Name,
                    ToBlockName = fallThrough,
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
        ControlFlowGraph cfg, KitX.Core.Contract.Workflow.Blueprint blueprint,
        Dictionary<string, BlueprintNode> nodeById, List<BlueprintConnection> execConns)
    {
        foreach (var block in cfg.Blocks)
        {
            if (block.FallThroughTarget != null) continue;  // Already has a Sequential edge
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
                // Single source of truth: build the Sequential edge (no parallel NextBlockName field).
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
                block.Type = lastStmt.FlowControlShape switch
                {
                    FlowControlType.ConditionalJump => CFGBlockType.BranchHeader,
                    // v5.0: ForLoop blocks stay Basic (LoopHeader type removed); loop structure
                    // is expressed via Goto back-edges, not dedicated block types.
                    _ => block.Type
                };
            }

            // v5.0: LoopBody classification removed — loop bodies are plain Basic blocks.
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 7: Set Parent Loop References
    // ════════════════════════════════════════════════════════════════════

    private static void SetParentLoopReferences(ControlFlowGraph cfg)
    {
        // v5.0: the v4.0 LoopbackToCondition-edge and LoopBody-edge based parent-loop resolution
        // is removed (those edge/block types are gone). ForLoop's body is a plain block that
        // re-enters via Goto (Sequential edge); ParentLoopBlockName is no longer populated by
        // this pass. Left as a no-op for any future loop-structure analysis that may need it.
    }

    // ════════════════════════════════════════════════════════════════════
    // Statement Generation
    // ════════════════════════════════════════════════════════════════════

    private CFGStatement? GenerateStatement(
        BlueprintNode node, Dictionary<string, BlueprintNode> nodeById,
        List<BlueprintConnection> execConns, KitX.Core.Contract.Workflow.Blueprint blueprint)
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
        List<BlueprintConnection> execConns, KitX.Core.Contract.Workflow.Blueprint blueprint)
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
                string methodName;
                string fullMethodName;
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
                    methodName = "PluginCallWithTarget";
                    fullMethodName = "PluginCallWithTarget";
                }
                else
                {
                    // 本地调用：PluginName.MethodName(args...)
                    var funcRef = string.IsNullOrEmpty(call.PluginName)
                        ? call.FunctionName : $"{call.PluginName}.{call.FunctionName}";
                    sourceCode = $"{funcRef}({callArgs})";
                    methodName = call.FunctionName;
                    fullMethodName = funcRef;
                }
                var callStmt = MakeCallStatement(sourceCode, methodName, fullMethodName);
                // Phase 2.1: build the `> pubVar` suffix at construction time (preferred over
                // PostProcessCallReturn rewrite). Same result, no post-hoc mutation.
                var consumedPubVar = TryGetConsumedPubVar(node);
                if (consumedPubVar != null)
                {
                    callStmt.SourceCode = $"{callStmt.Expression} > {consumedPubVar};";
                    callStmt.AssignedVariable = consumedPubVar;
                }
                return callStmt;
            }
            case BlueprintNodeType.CallHelper:
            {
                if (node is not CallHelperNode callHelper) return null;
                var helperArgs = _exportHelper.GetInputArgs(callHelper);
                var expression = $"{callHelper.HelperFunctionName}({helperArgs})";
                var helperStmt = MakeCallStatement(expression, callHelper.HelperFunctionName, callHelper.HelperFunctionName);
                // Phase 2.1: build the `> pubVar` suffix at construction time.
                var helperPubVar = TryGetConsumedPubVar(node);
                if (helperPubVar != null)
                {
                    helperStmt.SourceCode = $"{helperStmt.Expression} > {helperPubVar};";
                    helperStmt.AssignedVariable = helperPubVar;
                }
                return helperStmt;
            }
            case BlueprintNodeType.BuiltinFunction:
            {
                if (node is BuiltinFunctionNode bfNode
                    && _builtinFunctionStrategies.TryGetValue(bfNode.FunctionName, out var bfStrategy))
                {
                    var blockStmt = bfStrategy.ToStatement(node, _exportHelper);
                    if (blockStmt != null) return blockStmt;
                    // v5.0: flow-control functions (Branch/ForLoop/Goto/Switch/Break) whose
                    // ToStatement returns null must become FlowControlStatements so
                    // ConvertBlockStatementToCfgStatement sets FlowControlShape and arms get
                    // resolved. Without this, Goto round-tripped as `Goto()` (ExpressionStatement)
                    // with no target (Test D/H/I/K DIFF).
                    if (bfStrategy.FlowControlShape != null && bfStrategy.FlowControlShape != null)
                    {
                        var shape = bfStrategy.FlowControlShape.Value;
                        var fcStmt = new FlowControlStatement
                        {
                            ControlType = shape,
                            SourceCode = FlowControlStatement.RenderSource(
                                shape, string.Empty, new List<BranchArm>(), null),
                            LineNumber = 1
                        };
                        return fcStmt;
                    }
                    // ToStatement returned null — build a generic ExpressionStatement
                    // from the node's function name and input pin values instead of
                    // silently dropping the node (which would leave orphaned PubVar
                    // references and cause CS0103 in downstream code generation).
                    Log.Debug("[BP2CFGConverter] BuiltinFunction '{FuncName}' ToStatement returned null, " +
                        "using generic expression fallback", bfNode.FunctionName);
                    return BuildGenericBuiltinStatement(bfNode, bfNode.FunctionName);
                }
                return null;
            }
            // Const / Variable / other node types carry no executable statement — the former
            // _strategies fallback is gone (it was always empty: every node type either has an
            // explicit case above or is a pure data node that produces no BlockStatement).
            // v5.0: VariableNode write sites (incoming Value data edge) are handled above; a
            // VariableNode with no incoming Value edge is a floating read/declaration and
            // produces no statement (its value flows inline via downstream argument resolution).
            case BlueprintNodeType.Variable:
            {
                if (node is not VariableNode varNode) return null;
                // Write-site VariableNodes carry Exec pins (added in CFG2BPConverter); only those
                // emit `rhs > VarName`. Floating declaration/read VariableNodes (no Exec pin)
                // produce no statement.
                bool isWriteSite = varNode.InputPins.Any(p => p.Type == PinType.Execution);
                if (!isWriteSite) return null;

                var rhs = _exportHelper.GetInputValue(varNode, "Value");
                if (string.IsNullOrEmpty(rhs)) rhs = "null";
                var target = varNode.VarName ?? "";
                var assignExpr = $"{rhs} > {target}";
                return new ExpressionStatement
                {
                    Expression = assignExpr,
                    SourceCode = $"{assignExpr};",
                    AssignedVariable = target
                };
            }
            default:
                Log.Debug("[BP2CFGConverter] Node type {NodeType} produces no statement", node.NodeType);
                return null;
        }
    }

    /// <summary>
    /// Builds an <see cref="ExpressionStatement"/> for a blueprint call, attaching a
    /// <see cref="BSCall"/> so downstream <see cref="ConvertBlockStatementToCfgStatement"/>
    /// reads <c>FunctionName</c> directly from the AST instead of re-parsing <c>SourceCode</c>.
    /// Args are not modeled structurally here (the CFG consumes argument strings from input pins);
    /// only MethodName/FullMethodName/SourceText are needed.
    /// </summary>
    private static ExpressionStatement MakeCallStatement(string sourceCode, string methodName, string fullMethodName)
        => new()
        {
            Expression = sourceCode,
            SourceCode = sourceCode + ";",
            LineNumber = 1,
            ParsedExpression = new BSCall
            {
                MethodName = methodName,
                FullMethodName = fullMethodName,
                SourceText = sourceCode
            }
        };

    /// <summary>
    /// Returns the PubVar name assigned to this node's consumed data output, or null when the
    /// output is not consumed downstream. Centralises the ConsumedOutputs + FindOutputPubVar
    /// lookup so call sites build the `> pubVar` suffix at construction time (Phase 2.1 digestion
    /// of the former PostProcessCallReturn post-processing patch).
    /// </summary>
    private string? TryGetConsumedPubVar(BlueprintNode node)
    {
        if (_currentCtx == null) return null;

        if (node.NodeType is BlueprintNodeType.Call or BlueprintNodeType.CallHelper)
        {
            var returnPin = node.OutputPins.FirstOrDefault(p => p.Name == Return);
            bool hasReturn = returnPin != null
                && _currentCtx.ConsumedOutputs.Contains((node.Id, Return));
            return hasReturn ? NodeExportHelper.FindOutputPubVar(node, Return, _currentCtx) : null;
        }

        if (node.NodeType == BlueprintNodeType.BuiltinFunction)
        {
            var dataOut = node.OutputPins.FirstOrDefault(p => p.Type != PinType.Execution);
            if (dataOut != null && _currentCtx.ConsumedOutputs.Contains((node.Id, dataOut.Name)))
                return NodeExportHelper.FindOutputPubVar(node, dataOut.Name, _currentCtx);
        }

        return null;
    }

    private ConversionContext? _currentCtx;

    /// <summary>
    /// Sets the context for PubVar resolution during statement generation.
    /// Called before Build() when BlockScopes path is used.
    /// </summary>
    public void SetContext(KitX.Core.Contract.Workflow.Blueprint blueprint, ConversionContext? ctx)
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
                cfgStmt.Kind = ControlFlowMapping.ToKind(flow.ControlType);
                cfgStmt.FlowControlShape = flow.ControlType;
                cfgStmt.FunctionName = ControlFlowMapping.ToFunctionName(flow.ControlType);
                if (string.IsNullOrEmpty(cfgStmt.FunctionName))
                    cfgStmt.FunctionName = null;
                cfgStmt.ConditionExpression = flow.ConditionExpression;
                // Copy the full arm list so N-way Switch and any variadic shape survive.
                // ToLoopCond's loopback target lives in Arms[0] (IsLoopback=true), so it is
                // carried by this clone — no separate field copy needed.
                cfgStmt.Arms = flow.Arms.Select(a => a.Clone()).ToList();
                break;

            case ExpressionStatement expr:
                cfgStmt.OriginalExpression = expr.SourceCode;

                // Kind & FunctionName from builtin function metadata (data-driven, no adapter layer)
                if (node is BuiltinFunctionNode bfn
                    && _builtinFunctionStrategies.TryGetValue(bfn.FunctionName, out var bfDef))
                {
                    cfgStmt.Kind = bfDef.FlowControlShape switch
                    {
                        FlowControlType.ConditionalJump => CFGStatementKind.Branch,
                        FlowControlType.IterativeCounted => CFGStatementKind.ForLoop,
                        FlowControlType.UnconditionalJump => CFGStatementKind.Goto,
                        FlowControlType.IndexedDispatch => CFGStatementKind.Switch,
                        FlowControlType.LoopExit => CFGStatementKind.Break,
                        _ => CFGStatementKind.Expression
                    };
                    cfgStmt.FunctionName = bfDef.FunctionName;
                }
                else
                {
                    cfgStmt.Kind = CFGStatementKind.Expression;
                    // FunctionName from the pre-built BS call attached by GenerateBlockStatement
                    // (Call/CallHelper nodes carry a BSCall in ParsedExpression).
                    if (expr.ParsedExpression is BSCall bsCall)
                        cfgStmt.FunctionName = bsCall.MethodName;
                }

                // v5.0: if the ExpressionStatement carries an explicit AssignedVariable
                // (e.g. pure assignment via VariableNode write site, `rhs > var`), surface
                // it as PubVarTarget so the CFG→BS round-trip preserves the assignment.
                if (!string.IsNullOrEmpty(expr.AssignedVariable))
                {
                    cfgStmt.PubVarTarget = expr.AssignedVariable;
                    if (cfgStmt.Kind == CFGStatementKind.Expression)
                        cfgStmt.Kind = CFGStatementKind.Assignment;
                }

                // Arguments from node input pins (no text parsing)
                cfgStmt.Arguments = new List<string>();
                foreach (var pin in node.InputPins)
                {
                    if (pin.Name != "Exec")
                        cfgStmt.Arguments.Add(_exportHelper.GetInputValue(node, pin.Name));
                }

                // PubVarTarget from consumed output lookup (complement to AssignedVariable above;
                // handles function-call assignments where the PubVar is found via output pin).
                if (_currentCtx != null && string.IsNullOrEmpty(cfgStmt.PubVarTarget))
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

    /// <summary>
    /// v5.0: synthesises a Goto statement for the WalkNode loopback path (formerly a v4.0
    /// ToLoopCond statement). The target lives in Arms[0].TargetBlockName.
    /// </summary>
    private static CFGStatement CreateToLoopCondStatement(string? returnTo)
    {
        var stmt = new CfgStatementBuilder
        {
            // BlockName is set by the caller (WalkNode) after this returns; empty here matches
            // the pre-builder default.
            BlockName = string.Empty,
            FlowControlShape = FlowControlType.UnconditionalJump,
            // Loopback target carried in Arms[0] (IsLoopback=true) so the builder's default
            // renderer produces Goto("target"); via RenderSource.
            Arms = returnTo != null
                ? [new BranchArm { TargetBlockName = returnTo, IsLoopback = true }]
                : [],
            SourceLine = 1,
        }.Build();
        return stmt;
    }

    /// <summary>
    /// Fallback: builds a generic ExpressionStatement for a BuiltinFunctionNode whose
    /// ToStatement strategy returns null. Collects non-exec input pin values in pin order
    /// and emits <c>FunctionName(arg1, arg2, ...)</c>. Handles PubVar assignment when
    /// a data output pin is consumed by downstream connections.
    /// </summary>
    private BlockStatement? BuildGenericBuiltinStatement(BuiltinFunctionNode bfNode, string functionName)
    {
        var argPins = bfNode.InputPins
            .Where(p => p.Type != PinType.Execution)
            .ToList();

        var args = argPins
            .Select(p => _exportHelper.GetInputValue(bfNode, p.Name))
            .ToList();

        var expr = $"{functionName}({string.Join(", ", args)})";

        // Determine if an output PubVar assignment is needed (Return/Result pin consumed by downstream connections)
        string? pubVar = null;
        foreach (var pin in bfNode.OutputPins.Where(p => p.Type != PinType.Execution))
        {
            var pv = NodeExportHelper.FindOutputPubVar(bfNode, pin.Name, _currentCtx);
            if (!string.IsNullOrEmpty(pv))
            {
                pubVar = pv;
                break;
            }
        }

        var sourceCode = !string.IsNullOrEmpty(pubVar)
            ? $"{pubVar} = {expr};"
            : $"{expr};";

        return new ExpressionStatement
        {
            Expression = expr,
            SourceCode = sourceCode,
            LineNumber = 1,
            ParsedExpression = new BSCall
            {
                MethodName = functionName,
                FullMethodName = functionName,
                SourceText = expr
            }
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Step 3.5: Resolve Branch/Loop Target Block Names
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After blocks are built, resolves the outgoing arms (<see cref="CFGStatement.Arms"/>)
    /// of Branch / Loop / Switch statements from Blueprint execution connections.
    /// Strategy-generated statements don't know their target block names;
    /// we derive them from the node's output pins → connections → target node → containing block.
    /// Each output execution pin maps to one arm keyed by the pin's <see cref="BlueprintPin.Name"/>,
    /// so N-way Switch arms (Default/0/1/...) are preserved without positional loss.
    /// </summary>
    private static void ResolveControlFlowTargets(
        ControlFlowGraph cfg,
        KitX.Core.Contract.Workflow.Blueprint blueprint,
        Dictionary<string, BlueprintNode> nodeById,
        List<BlueprintConnection> execConns)
    {
        foreach (var block in cfg.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                if (stmt.FlowControlShape is not (FlowControlType.ConditionalJump
                    or FlowControlType.IterativeCounted or FlowControlType.IndexedDispatch
                    or FlowControlType.UnconditionalJump))
                    continue;
                if (stmt.Arms.Count > 0) continue;

                if (!nodeById.TryGetValue(stmt.StatementId, out var node))
                    continue;

                ResolveControlFlowTargetsForNode(stmt, node, cfg, blueprint, execConns);
            }
        }
    }

    private static void ResolveControlFlowTargetsForNode(
        CFGStatement stmt, BlueprintNode cfNode,
        ControlFlowGraph cfg, KitX.Core.Contract.Workflow.Blueprint blueprint,
        List<BlueprintConnection> execConns)
    {
        // Map each output execution pin → (pinName, targetBlock), appending one arm per pin.
        // Pin name becomes the arm key, so Branch(True/False), Loop(LoopBody/LoopEnd) and
        // Switch(Default/0/1/...) all preserve their identity with no positional loss.
        foreach (var pin in cfNode.OutputPins.Where(p => p.Type == PinType.Execution))
        {
            var conn = execConns.FirstOrDefault(c => c.SourcePinId == pin.Id);
            if (conn == null) continue;

            var targetBlock = FindBlockContainingNode(cfg, conn.TargetNodeId, blueprint);
            if (targetBlock == null) continue;

            stmt.Arms.Add(new BranchArm
            {
                PinName = pin.Name,
                TargetBlockName = targetBlock
            });
        }

        if (stmt.Arms.Count > 0)
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
        KitX.Core.Contract.Workflow.Blueprint blueprint)
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

    private static string RegenerateBranchSource(CFGStatement cfStmt)
        // Delegate to the shared renderer on FlowControlStatement so BS↔graph source text stays
        // in sync with the instance RegenerateSourceCode path (single source of truth).
        // v5.0: Goto (UnconditionalJump) carries its target in Arms[0].TargetBlockName — pass it
        // v5.0: RenderSource uses Arms[0].TargetBlockName for UnconditionalJump (Goto target).
        => FlowControlStatement.RenderSource(
            cfStmt.FlowControlShape ?? FlowControlType.ConditionalJump,
            cfStmt.ConditionExpression ?? string.Empty,
            cfStmt.Arms);

    private static CFGEdgeType GetEdgeType(FlowControlType? shape, BranchArm arm)
    {
        // v5.0: loopback arms (v4.0 ToLoopCond) now use Sequential (Goto back-edge).
        if (arm.IsLoopback) return CFGEdgeType.Sequential;

        // Derive edge semantics from the pin name so the mapping is data-driven rather than
        // positional. Handles ConditionalJump (True/False), IterativeCounted (ForLoop
        // LoopBody/LoopEnd), UnconditionalJump (Goto → Sequential), and IndexedDispatch
        // (Default/0/1/...) uniformly.
        return (shape, arm.PinName) switch
        {
            // v5.0 Goto: unconditional jump uses a plain Sequential edge (the unified
            // fall-through/back-edge mechanism, §7.6). Goto's single "Exec" arm lands here.
            (FlowControlType.UnconditionalJump, _) => CFGEdgeType.Sequential,

            // v5.0 ForLoop: LoopBody/LoopEnd arms.
            (FlowControlType.IterativeCounted, "LoopBody") => CFGEdgeType.LoopBody,
            (FlowControlType.IterativeCounted, "LoopEnd") => CFGEdgeType.LoopExit,

            (FlowControlType.IndexedDispatch, _) => CFGEdgeType.Switch,
            (_, "False") => CFGEdgeType.BranchFalse,
            (_, "LoopEnd") => CFGEdgeType.LoopExit,
            _ => CFGEdgeType.BranchTrue
        };
    }

    // ─── Node Type Helpers ────────────────────────────────────────────

    private bool IsControlFlowNode(BlueprintNode node) =>
        node is BuiltinFunctionNode bfn
        && _builtinFunctionStrategies.TryGetValue(bfn.FunctionName, out var def)
        && def.FlowControlShape != null;
}
