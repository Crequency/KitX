using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using Serilog;

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

        // Add ConstValues
        foreach (var kvp in context.ConstNodes)
        {
            bp.ConstValues.Add(new VariableConstant
            {
                Name = kvp.Value.ConstName,
                DefaultValue = kvp.Value.ConstValue,
                Type = kvp.Value.ConstType ?? "string"
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
}
