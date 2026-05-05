using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint.CFG;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using Serilog;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// Phase 4+5: Creates data edges from ControlFlowGraph argument analysis.
/// Handles PubVar references, ConstBlock connections, DefaultValues, and data edge dedup.
/// </summary>
public class DataEdgeBuilder
{
    public void Build(PipelineContext context)
    {
        // Process all statements in all blocks
        foreach (var block in context.FormattedScript.Blocks)
        {
            foreach (var stmt in block.Statements)
            {
                ProcessStatement(stmt, context);
            }
        }

        // Deduplicate data edges (Phase 5)
        DeduplicateDataEdges(context);

        Log.Debug("[DataEdgeBuilder] Done: {DataEdgeCount} data edges",
            context.DataEdges.Count);
    }

    // ──────────────────────────────────────────────
    // Statement processing
    // ──────────────────────────────────────────────

    private void ProcessStatement(CFGStatement stmt, PipelineContext context)
    {
        switch (stmt.Kind)
        {
            case CFGStatementKind.Assignment:
            case CFGStatementKind.Expression:
                ProcessCallArguments(stmt, context);
                break;

            case CFGStatementKind.Print:
                ProcessSingleValueInput(stmt, stmt.Arguments, Value, context);
                break;

            case CFGStatementKind.Set:
                ProcessSetValue(stmt, context);
                break;

            case CFGStatementKind.Pause:
                ProcessSingleValueInput(stmt, stmt.Arguments, "Milliseconds", context);
                break;

            case CFGStatementKind.Branch:
                ProcessConditionInput(stmt, context);
                break;

            case CFGStatementKind.Loop:
                ProcessConditionInput(stmt, context);
                break;
        }
    }

    // ──────────────────────────────────────────────
    // Call/Assignment argument processing
    // ──────────────────────────────────────────────

    private void ProcessCallArguments(CFGStatement stmt, PipelineContext context)
    {
        if (!context.NodeByStatementId.TryGetValue(stmt.StatementId, out var targetNode)) return;
        if (stmt.Arguments == null) return;

        for (int i = 0; i < stmt.Arguments.Count; i++)
        {
            var arg = stmt.Arguments[i];
            var pinName = GetParamPinName(targetNode, i);
            if (pinName == null) continue;

            ProcessArgument(arg, targetNode, pinName, stmt, i, context);
        }
    }

    /// <summary>
    /// Processes a single argument expression and creates a data edge or sets DefaultValue.
    /// </summary>
    private void ProcessArgument(string arg, BlueprintNode targetNode, string targetPinName,
        CFGStatement parentStmt, int argIndex, PipelineContext context)
    {
        var trimmed = arg.Trim();

        // String literal → DefaultValue
        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
        {
            var value = trimmed[1..^1];
            SetDefaultValue(targetNode, targetPinName, value);
            return;
        }

        // Character literal → DefaultValue (pass through as-is, e.g. '\0')
        if (ExprUtils.IsCharacterLiteral(trimmed))
        {
            SetDefaultValue(targetNode, targetPinName, trimmed);
            return;
        }

        // Numeric literal → DefaultValue
        if (int.TryParse(trimmed, out _) || double.TryParse(trimmed, out _))
        {
            SetDefaultValue(targetNode, targetPinName, trimmed);
            return;
        }

        // PubVar reference → find PubVarAssignment source
        if (context.PubVarNames.Contains(trimmed))
        {
            ConnectPubVarSource(trimmed, targetNode, targetPinName, context);
            return;
        }

        // ConstBlock variable → data edge from ConstNode.Value
        if (context.ConstNodes.TryGetValue(trimmed, out var constNode))
        {
            context.DataEdges.Add(new PendingDataEdge
            {
                SourceNodeId = constNode.Id,
                SourcePinName = Value,
                TargetNodeId = targetNode.Id,
                TargetPinName = targetPinName
            });
            return;
        }

        // VariableNode (no initial value) → set as DefaultValue fallback on target pin
        // VariableNodes have no output ports, so we can't create a data edge.
        // The variable reference is resolved at runtime.
        if (context.VariableNodes.ContainsKey(trimmed))
        {
            SetDefaultValue(targetNode, targetPinName, trimmed);
            return;
        }

        // Unknown identifier → try as variable reference (set DefaultValue as fallback)
        Log.Warning("[DataEdgeBuilder] Unresolved argument: {Arg} in stmt {StmtId}", trimmed, parentStmt.StatementId);
        SetDefaultValue(targetNode, targetPinName, trimmed);
    }

    // ──────────────────────────────────────────────
    // Special node argument processing
    // ──────────────────────────────────────────────

    private void ProcessSingleValueInput(CFGStatement stmt, List<string>? args,
        string pinName, PipelineContext context)
    {
        if (args == null || args.Count == 0) return;
        if (!context.NodeByStatementId.TryGetValue(stmt.StatementId, out var targetNode)) return;

        ProcessArgument(args[0], targetNode, pinName, stmt, 0, context);
    }

    private void ProcessSetValue(CFGStatement stmt, PipelineContext context)
    {
        if (!context.NodeByStatementId.TryGetValue(stmt.StatementId, out var targetNode)) return;
        if (stmt.Arguments == null || stmt.Arguments.Count == 0) return;

        // Set's Value pin receives the second argument (first was extracted as SetVarName)
        if (stmt.Arguments.Count > 0)
        {
            ProcessArgument(stmt.Arguments[0], targetNode, Value, stmt, 0, context);
        }
    }

    private void ProcessConditionInput(CFGStatement stmt, PipelineContext context)
    {
        if (string.IsNullOrEmpty(stmt.ConditionPubVar)) return;
        if (!context.NodeByStatementId.TryGetValue(stmt.StatementId, out var targetNode)) return;

        // Condition PubVar → find the PubVarAssignment source and connect to Condition pin
        ConnectPubVarSource(stmt.ConditionPubVar, targetNode, Condition, context);
    }

    // ──────────────────────────────────────────────
    // PubVar source connection
    // ──────────────────────────────────────────────

    private void ConnectPubVarSource(string pubVarName, BlueprintNode targetNode,
        string targetPinName, PipelineContext context)
    {
        // Find the PubVarAssignment that produces this PubVar
        foreach (var kvp in context.PubVarAssignments)
        {
            if (kvp.Value.PubVarName == pubVarName)
            {
                context.DataEdges.Add(new PendingDataEdge
                {
                    SourceNodeId = kvp.Value.SourceNode.Id,
                    SourcePinName = kvp.Value.SourcePin.Name,
                    TargetNodeId = targetNode.Id,
                    TargetPinName = targetPinName,
                    PubVarName = pubVarName
                });
                return;
            }
        }

        Log.Warning("[DataEdgeBuilder] PubVar source not found: {PubVar}", pubVarName);
    }

    // ──────────────────────────────────────────────
    // Data edge deduplication (Phase 5)
    // ──────────────────────────────────────────────

    private void DeduplicateDataEdges(PipelineContext context)
    {
        var unique = new Dictionary<string, PendingDataEdge>();

        foreach (var edge in context.DataEdges)
        {
            // Key by (source node, source pin, target node, target pin, pubvar)
            var key = $"{edge.SourceNodeId}|{edge.SourcePinName}|{edge.TargetNodeId}|{edge.TargetPinName}|{edge.PubVarName ?? ""}";
            if (!unique.ContainsKey(key))
                unique[key] = edge;
        }

        var beforeCount = context.DataEdges.Count;
        context.DataEdges = unique.Values.ToList();

        if (beforeCount != context.DataEdges.Count)
        {
            Log.Debug("[DataEdgeBuilder] Dedup: {Before} → {After} data edges", beforeCount, context.DataEdges.Count);
        }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static void SetDefaultValue(BlueprintNode node, string pinName, string value)
    {
        var pin = node.InputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin != null)
            pin.DefaultValue = value;
    }

    private static string? GetParamPinName(BlueprintNode node, int argIndex)
    {
        // Find parameter pins (skip Exec pin at index 0)
        var paramPins = node.InputPins.Where(p => p.Name != Exec).ToList();
        if (argIndex < paramPins.Count)
            return paramPins[argIndex].Name;
        return null;
    }

}
