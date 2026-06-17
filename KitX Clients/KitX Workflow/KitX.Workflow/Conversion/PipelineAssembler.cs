using KitX.Core.Contract.Workflow;
using Serilog;

using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Phase 6: Assembles the final Blueprint from all pipeline outputs.
/// Converts PendingExecEdges and PendingDataEdges into BlueprintConnections.
/// </summary>
public class PipelineAssembler
{
    private PipelineContext _ctx = null!;
    private Dictionary<string, BlueprintNode> _nodeById = new();

    public KitX.Core.Contract.Workflow.Blueprint Assemble(PipelineContext context)
    {
        _ctx = context;

        var bp = new KitX.Core.Contract.Workflow.Blueprint
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

    private void BuildBlockScopes(KitX.Core.Contract.Workflow.Blueprint bp, PipelineContext ctx)
    {
        var mainBlockName = ctx.FormattedScript.MainBlockName;

        // Build a BlockScope for each formatted block — no merging.
        // Each #Block in BlockScript is an explicit semantic boundary chosen by the user;
        // preserving them ensures BP↔BS round-trip fidelity and avoids semantic errors
        // (e.g. ToLoopCond returning to a block that re-initializes variables).
        var assignedNodeIds = new HashSet<string>();
        foreach (var block in ctx.FormattedScript.Blocks)
        {
            var nodeIds = new List<string>();
            if (ctx.BlockNodeIds.TryGetValue(block.Name, out var ids))
                nodeIds = ids.Where(id => assignedNodeIds.Add(id)).ToList();

            bp.BlockScopes.Add(new BlueprintBlockScope
            {
                Name = block.Name,
                IsMainBlock = block.Name == mainBlockName,
                NextBlockName = block.NextBlockName,
                NodeIds = nodeIds,
            });
        }

        // Assign ownership from DeferredEdges (Branch/Loop/ToLoopCond etc.)
        var scopesByName = bp.BlockScopes.ToDictionary(s => s.Name, s => s);
        foreach (var deferred in ctx.DeferredEdges)
        {
            if (!ctx.NodeByStatementId.TryGetValue(deferred.SourceStatementId, out var ownerNode)) continue;

            foreach (var (pinName, targetBlockName) in deferred.Arms)
            {
                if (targetBlockName == null) continue;
                if (!scopesByName.TryGetValue(targetBlockName, out var scope)) continue;
                scope.OwnerNodeId = ownerNode.Id;
                scope.OwnerArmName = pinName;
            }
        }

        Log.Debug("[PipelineAssembler] Built {ScopeCount} block scopes",
            bp.BlockScopes.Count);
    }

}
