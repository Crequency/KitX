using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using Serilog;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// Phase 6: Assembles the final Blueprint from all pipeline outputs.
/// Converts PendingExecEdges and PendingDataEdges into BlueprintConnections.
/// </summary>
public class PipelineAssembler
{
    private PipelineContext _ctx = null!;
    private Dictionary<string, BlueprintNode> _nodeById = new();

    public Contract.Workflow.Blueprint Assemble(PipelineContext context)
    {
        _ctx = context;

        var bp = new Contract.Workflow.Blueprint
        {
            Name = "Imported from BlockScript",
            PubVarNames = new List<string>(context.PubVarNames),
            HelperFunctions = context.HelperFunctions,
        };

        // Add ConstValues (from ConstNodes with initial values)
        foreach (var kvp in context.ConstNodes)
        {
            bp.ConstValues.Add(new VariableConstant
            {
                Name = kvp.Value.ConstName,
                DefaultValue = kvp.Value.ConstValue,
                Type = kvp.Value.ConstType ?? "string"
            });
        }

        // Add VariableNodes to ConstValues (no initial value, type-only)
        foreach (var kvp in context.VariableNodes)
        {
            bp.ConstValues.Add(new VariableConstant
            {
                Name = kvp.Value.VarName,
                DefaultValue = null,
                Type = kvp.Value.VarType ?? "int"
            });
        }

        // Add all nodes + build lookup
        foreach (var node in context.AllNodes)
        {
            bp.AddNode(node);
            _nodeById[node.Id] = node;
        }

        // Convert exec edges to connections
        foreach (var edge in context.ExecEdges)
        {
            var conn = CreateExecConnection(edge);
            if (conn != null)
                bp.AddConnection(conn);
        }

        // Convert data edges to connections
        foreach (var edge in context.DataEdges)
        {
            var conn = CreateDataConnection(edge);
            if (conn != null)
                bp.AddConnection(conn);
        }

        Log.Debug("[PipelineAssembler] Assembled: {NodeCount} nodes, {ConnCount} connections",
            bp.Nodes.Count, bp.Connections.Count);

        // Build block scopes from pipeline context
        BuildBlockScopes(bp, context);

        return bp;
    }

    // ──────────────────────────────────────────────
    // Exec edge → BlueprintConnection
    // ──────────────────────────────────────────────

    private BlueprintConnection? CreateExecConnection(PendingExecEdge edge)
    {
        var sourceNode = ResolveNodeByStmtId(edge.SourceStatementId);
        var targetNode = ResolveNodeByStmtId(edge.TargetStatementId);

        if (sourceNode == null || targetNode == null)
        {
            Log.Warning("[Assembler] Exec edge node not resolved: {Src} → {Tgt}",
                edge.SourceStatementId, edge.TargetStatementId);
            return null;
        }

        var sourcePin = FindOutputPin(sourceNode, edge.SourcePinName);
        var targetPin = FindInputPin(targetNode, edge.TargetPinName);

        if (sourcePin == null || targetPin == null)
        {
            Log.Warning("[Assembler] Pin not found: {Src}.{SrcPin} → {Tgt}.{TgtPin}",
                sourceNode.Name, edge.SourcePinName, targetNode.Name, edge.TargetPinName);
            return null;
        }

        return new BlueprintConnection
        {
            SourceNodeId = sourceNode.Id,
            SourcePinId = sourcePin.Id,
            TargetNodeId = targetNode.Id,
            TargetPinId = targetPin.Id
        };
    }

    // ──────────────────────────────────────────────
    // Data edge → BlueprintConnection
    // ──────────────────────────────────────────────

    private BlueprintConnection? CreateDataConnection(PendingDataEdge edge)
    {
        if (!_nodeById.TryGetValue(edge.SourceNodeId, out var sourceNode) ||
            !_nodeById.TryGetValue(edge.TargetNodeId, out var targetNode))
        {
            Log.Warning("[Assembler] Data edge node not found: {Src} → {Tgt}",
                edge.SourceNodeId, edge.TargetNodeId);
            return null;
        }

        var sourcePin = FindOutputPin(sourceNode, edge.SourcePinName);
        var targetPin = FindInputPin(targetNode, edge.TargetPinName);

        if (sourcePin == null || targetPin == null)
        {
            Log.Warning("[Assembler] Data pin not found: {Src}.{SPin} → {Tgt}.{TPin}",
                sourceNode.Name, edge.SourcePinName, targetNode.Name, edge.TargetPinName);
            return null;
        }

        return new BlueprintConnection
        {
            SourceNodeId = sourceNode.Id,
            SourcePinId = sourcePin.Id,
            TargetNodeId = targetNode.Id,
            TargetPinId = targetPin.Id,
            PubVarName = edge.PubVarName
        };
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private BlueprintNode? ResolveNodeByStmtId(string stmtId)
    {
        if (stmtId == "__entry__")
            return _ctx.EntryNode;

        _ctx.NodeByStatementId.TryGetValue(stmtId, out var node);
        return node;
    }

    private static BlueprintPin? FindOutputPin(BlueprintNode node, string pinName)
        => node.OutputPins.FirstOrDefault(p => p.Name == pinName);

    private static BlueprintPin? FindInputPin(BlueprintNode node, string pinName)
        => node.InputPins.FirstOrDefault(p => p.Name == pinName);

    // ──────────────────────────────────────────────
    // Block Scope Construction
    // ──────────────────────────────────────────────

    private void BuildBlockScopes(Contract.Workflow.Blueprint bp, PipelineContext ctx)
    {
        var mainBlockName = ctx.FormattedScript.MainBlockName;

        // Identify which blocks are Branch/Loop targets (have owners)
        var ownedBlockNames = new HashSet<string>();
        foreach (var (_, trueBlock, falseBlock) in ctx.BranchDefs)
        {
            if (trueBlock != null) ownedBlockNames.Add(trueBlock);
            if (falseBlock != null) ownedBlockNames.Add(falseBlock);
        }
        foreach (var (_, loopBody, loopEnd, _) in ctx.LoopDefs)
        {
            if (loopBody != null) ownedBlockNames.Add(loopBody);
            if (loopEnd != null) ownedBlockNames.Add(loopEnd);
        }

        // Build temporary scopes for all formatted blocks
        var tempScopes = new Dictionary<string, (BlueprintBlockScope scope, List<string> nodeIds, string? nextBlock)>();
        foreach (var block in ctx.FormattedScript.Blocks)
        {
            var scope = new BlueprintBlockScope
            {
                Name = block.Name,
                IsMainBlock = block.Name == mainBlockName,
                NextBlockName = block.NextBlockName,
            };

            var nodeIds = new List<string>();
            if (ctx.BlockNodeIds.TryGetValue(block.Name, out var ids))
                nodeIds = new List<string>(ids);

            tempScopes[block.Name] = (scope, nodeIds, block.NextBlockName);
        }

        // Merge sequential (non-owned) blocks into their parent chain
        // A block is "sequential" if it's not the main block and not owned by a Branch/Loop
        var merged = new HashSet<string>();
        foreach (var kvp in tempScopes.ToList())
        {
            var (scope, nodeIds, nextBlock) = kvp.Value;
            if (scope.IsMainBlock || ownedBlockNames.Contains(scope.Name))
                continue;

            // Find the root ancestor: follow NextBlockName chain upward to find the named/owned block
            var ancestorName = FindAncestor(tempScopes, kvp.Key, ownedBlockNames, mainBlockName);
            if (ancestorName != null && ancestorName != kvp.Key)
            {
                // Merge this scope's nodes into the ancestor
                var (ancestorScope, ancestorNodeIds, _) = tempScopes[ancestorName];
                ancestorNodeIds.AddRange(nodeIds);
                merged.Add(kvp.Key);
            }
        }

        // Remove merged scopes and add remaining to Blueprint.
        // Deduplicate: each node belongs to its FIRST occurrence block only.
        // This prevents shared data nodes (e.g., loop condition Compare nodes
        // duplicated into LoopBodyEnd blocks) from appearing in multiple scopes.
        var assignedNodeIds = new HashSet<string>();
        foreach (var kvp in tempScopes)
        {
            if (merged.Contains(kvp.Key)) continue;

            var (scope, nodeIds, _) = kvp.Value;
            // Filter out nodes already assigned to an earlier block
            var uniqueNodeIds = nodeIds.Where(id => assignedNodeIds.Add(id)).ToList();
            scope.NodeIds = uniqueNodeIds;
            bp.BlockScopes.Add(scope);
        }

        // Build scopesByName for ownership assignment
        var scopesByName = bp.BlockScopes.ToDictionary(s => s.Name, s => s);

        // Assign ownership from Branch definitions
        foreach (var (stmtId, trueBlock, falseBlock) in ctx.BranchDefs)
        {
            if (!ctx.NodeByStatementId.TryGetValue(stmtId, out var branchNode)) continue;

            if (trueBlock != null && scopesByName.TryGetValue(trueBlock, out var trueScope))
            {
                trueScope.OwnerNodeId = branchNode.Id;
                trueScope.OwnerArmName = True;
            }
            if (falseBlock != null && scopesByName.TryGetValue(falseBlock, out var falseScope))
            {
                falseScope.OwnerNodeId = branchNode.Id;
                falseScope.OwnerArmName = False;
            }
        }

        // Assign ownership from Loop definitions
        foreach (var (stmtId, loopBody, loopEnd, _) in ctx.LoopDefs)
        {
            if (!ctx.NodeByStatementId.TryGetValue(stmtId, out var loopNode)) continue;

            if (loopBody != null && scopesByName.TryGetValue(loopBody, out var bodyScope))
            {
                bodyScope.OwnerNodeId = loopNode.Id;
                bodyScope.OwnerArmName = LoopBody;
            }
            if (loopEnd != null && scopesByName.TryGetValue(loopEnd, out var endScope))
            {
                endScope.OwnerNodeId = loopNode.Id;
                endScope.OwnerArmName = LoopEnd;
            }
        }

        Log.Debug("[PipelineAssembler] Built {ScopeCount} block scopes (merged {MergedCount} intermediate)",
            bp.BlockScopes.Count, merged.Count);
    }

    /// <summary>
    /// Walks the NextBlockName chain backward from a block to find its named/owned ancestor.
    /// Returns null if the block is orphaned.
    /// </summary>
    private string? FindAncestor(
        Dictionary<string, (BlueprintBlockScope scope, List<string> nodeIds, string? nextBlock)> tempScopes,
        string blockName,
        HashSet<string> ownedBlockNames,
        string mainBlockName)
    {
        // Follow the chain: find which block has NextBlockName pointing to our block
        foreach (var kvp in tempScopes)
        {
            var (_, _, nextBlock) = kvp.Value;
            if (nextBlock == blockName)
            {
                // The parent is this block
                if (kvp.Key == mainBlockName || ownedBlockNames.Contains(kvp.Key))
                    return kvp.Key;
                // Recurse to find the grandparent
                return FindAncestor(tempScopes, kvp.Key, ownedBlockNames, mainBlockName);
            }
        }
        return null;
    }
}
