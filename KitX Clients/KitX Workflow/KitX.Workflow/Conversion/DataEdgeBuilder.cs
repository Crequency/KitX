using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;

using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using Serilog;
using KitX.Workflow.CFG;
using KitX.Workflow.Models;

using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Phase 4+5: Creates data edges from ControlFlowGraph argument analysis.
/// Handles PubVar references, ConstBlock connections, DefaultValues, and data edge dedup.
/// All argument-to-pin mapping is driven by IBuiltinFunctionDefinition.InputPins.
/// </summary>
public class DataEdgeBuilder
{
    private readonly BuiltinFunctionRegistry? _functionRegistry;

    public DataEdgeBuilder(BuiltinFunctionRegistry? functionRegistry = null)
    {
        _functionRegistry = functionRegistry;
    }

    public void Build(PipelineContext context)
    {
        foreach (var block in context.FormattedScript.Blocks)
        {
            // v5.0: iterate GetEffectiveStatements so PipelineStatement entries are transparently
            // flattened — DataEdgeBuilder sees only plain CFGStatements with resolved Arguments.
            foreach (var stmt in block.GetEffectiveStatements())
            {
                ProcessStatement(stmt, context);
            }
        }

        DeduplicateDataEdges(context);

        Log.Debug("[DataEdgeBuilder] Done: {DataEdgeCount} data edges",
            context.DataEdges.Count);
    }

    // ──────────────────────────────────────────────
    // Statement processing — registry-driven
    // ──────────────────────────────────────────────

    private void ProcessStatement(CFGStatement stmt, PipelineContext context)
    {
        if (!context.NodeByStatementId.TryGetValue(stmt.StatementId, out var targetNode)) return;

        // Use registry to determine pin mapping
        var funcDef = _functionRegistry?.Get(stmt.FunctionName ?? string.Empty);

        if (funcDef != null)
        {
            // Use the target node's actual input pins (may include dynamic pins added by
            // ConfigureNode, e.g. StringConcatFunction and PluginCallFunction). The
            // descriptor pins are a subset; reading from the node ensures extra arguments
            // map to dynamically added pins and survive the BP round-trip.
            var nonExecPins = targetNode.InputPins
                .Where(p => p.Type != PinType.Execution)
                .ToList();

            if (stmt.Arguments != null)
            {
                for (int i = 0; i < stmt.Arguments.Count && i < nonExecPins.Count; i++)
                {
                    ProcessArgument(stmt.Arguments[i], targetNode, nonExecPins[i].Name, stmt, i, context);
                }
            }

            // Connect ConditionPubVar to the flow-control node's condition/selector input pin.
            // This is the first non-Exec data input pin (Branch/Loop "Condition" is Boolean,
            // Switch "Selector" is Integer), so match by position rather than hard-coding Boolean.
            if (!string.IsNullOrEmpty(stmt.ConditionPubVar))
            {
                var condPin = funcDef.InputPins.FirstOrDefault(p => p.Type != PinType.Execution);
                if (condPin != null)
                    ConnectPubVarSource(stmt.ConditionPubVar, targetNode, condPin.Name, context);
            }
        }
        else
        {
            // Non-registry statements: generic argument processing
            if (stmt.Arguments != null)
            {
                for (int i = 0; i < stmt.Arguments.Count; i++)
                {
                    var pinName = GetParamPinName(targetNode, i);
                    if (pinName == null) continue;
                    ProcessArgument(stmt.Arguments[i], targetNode, pinName, stmt, i, context);
                }
            }

            // Handle condition for non-registry Branch/Loop
            if (!string.IsNullOrEmpty(stmt.ConditionPubVar))
            {
                ConnectPubVarSource(stmt.ConditionPubVar, targetNode, Condition, context);
            }
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

        // Character literal → DefaultValue
        if (BSExpressionExtensions.IsCharacterLiteral(trimmed))
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

        // VariableNode (floating declaration, no initial value). v5.0: pure-assignment writes
        // register a PubVarAssignment producer for the variable name (a write-site VariableNode);
        // prefer connecting to that producer so reads of an already-written variable flow as a
        // real data edge. Fall back to DefaultValue only for variables never written in this pass.
        if (context.VariableNodes.ContainsKey(trimmed))
        {
            var producer = context.PubVarAssignments.Values
                .FirstOrDefault(a => a.PubVarName == trimmed);
            if (producer != null)
            {
                context.DataEdges.Add(new PendingDataEdge
                {
                    SourceNodeId = producer.SourceNode.Id,
                    SourcePinName = producer.SourcePin.Name,
                    TargetNodeId = targetNode.Id,
                    TargetPinName = targetPinName,
                    PubVarName = trimmed
                });
            }
            else
            {
                SetDefaultValue(targetNode, targetPinName, trimmed);
            }
            return;
        }

        // Unknown identifier → try as variable reference
        Log.Warning("[DataEdgeBuilder] Unresolved argument: {Arg} in stmt {StmtId}", trimmed, parentStmt.StatementId);
        SetDefaultValue(targetNode, targetPinName, trimmed);
    }

    // ──────────────────────────────────────────────
    // PubVar source connection
    // ──────────────────────────────────────────────

    private void ConnectPubVarSource(string pubVarName, BlueprintNode targetNode,
        string targetPinName, PipelineContext context)
    {
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
        var paramPins = node.InputPins.Where(p => p.Name != Exec).ToList();
        if (argIndex < paramPins.Count)
            return paramPins[argIndex].Name;
        return null;
    }
}
