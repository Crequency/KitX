using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;

using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using Serilog;
using KitX.Workflow.CFG;
using KitX.Workflow.Models;

using KitX.Workflow.Blueprint;
using KitX.Workflow.Conversion.Arguments;
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
        // Phase 4: track the current block + effective-statement position so ProcessArgument can
        // query reaching-definitions for multi-writer variable resolution (Test K fix).
        foreach (var block in context.FormattedScript.Blocks)
        {
            _currentBlockName = block.Name;
            _currentPosition = 0;

            // v5.0: iterate GetEffectiveStatements so PipelineStatement entries are transparently
            // flattened — DataEdgeBuilder sees only plain CFGStatements with resolved Arguments.
            foreach (var stmt in block.GetEffectiveStatements())
            {
                ProcessStatement(stmt, context);
                _currentPosition++;
            }
        }

        DeduplicateDataEdges(context);

        Log.Debug("[DataEdgeBuilder] Done: {DataEdgeCount} data edges",
            context.DataEdges.Count);
    }

    // Current block + position for reaching-definitions queries during ProcessArgument.
    private string _currentBlockName = string.Empty;
    private int _currentPosition;

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

            // Connect the condition/selector source to the flow-control node's condition input pin.
            // This is the first non-Exec data input pin (Branch/Loop "Condition" is Boolean,
            // Switch "Selector" is Integer), so match by position rather than hard-coding Boolean.
            // The source PubVar name is carried by ConditionExpression (single identifier post-expansion).
            var condSrc = stmt.ConditionExpression?.Trim();
            if (!string.IsNullOrEmpty(condSrc) && context.PubVarNames.Contains(condSrc))
            {
                var condPin = funcDef.InputPins.FirstOrDefault(p => p.Type != PinType.Execution);
                if (condPin != null)
                    ConnectPubVarSource(condSrc, targetNode, condPin.Name, context);
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
            var nonRegCondSrc = stmt.ConditionExpression?.Trim();
            if (!string.IsNullOrEmpty(nonRegCondSrc) && context.PubVarNames.Contains(nonRegCondSrc))
            {
                ConnectPubVarSource(nonRegCondSrc, targetNode, Condition, context);
            }
        }
    }

    /// <summary>
    /// Processes a single argument expression and creates a data edge or sets DefaultValue.
    /// </summary>
    private void ProcessArgument(string arg, BlueprintNode targetNode, string targetPinName,
        CFGStatement parentStmt, int argIndex, PipelineContext context)
    {
        // Phase 3: use the shared ArgumentSourceClassifier so the string/char/numeric/identifier
        // predicates are not duplicated with NodeExportHelper.FormatLiteralValue.
        var source = ArgumentSourceClassifier.Classify(arg);

        // Literals (string/char/numeric/boolean) → set DefaultValue on the target pin.
        if (source.IsLiteral)
        {
            SetDefaultValue(targetNode, targetPinName,
                source.Kind == ArgumentSourceKind.StringLiteral
                    ? source.StrippedValue ?? source.RawValue  // string: strip outer quotes
                    : source.RawValue);                        // char/numeric/boolean: verbatim
            return;
        }

        // Call expressions are handled structurally by BS2CFGConverter.ExpandExpression —
        // ProcessArgument should not receive them (they'd have been pre-expanded into PubVars).
        // If one slips through, treat as opaque identifier fallback.
        var trimmed = source.RawValue;

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
            // Phase 4: delegate to ConnectPubVarSource which now uses reaching-definitions
            // to resolve the correct writer for multi-writer variables.
            ConnectPubVarSource(trimmed, targetNode, targetPinName, context);
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
        // Phase 4: use reaching-definitions analysis to resolve multi-writer variables. For a
        // consumer at (_currentBlockName, _currentPosition), find the definition of pubVarName
        // that reaches this program point, then connect to the Blueprint node that materialised
        // that definition. Falls back to first-match-wins when the analysis is unavailable or the
        // reaching definition's node can't be resolved (legacy path).
        if (context.ReachingDefinitions is { } reaching)
        {
            var site = reaching.ReachingAt(_currentBlockName, _currentPosition, pubVarName);
            if (site != null && context.NodeByStatementId.TryGetValue(site.Value.Statement.StatementId, out var producerNode))
            {
                // Find the producer's data output pin (Return / Value / first non-Exec).
                var producerPin = producerNode.OutputPins.FirstOrDefault(p => p.Type != PinType.Execution);
                if (producerPin != null)
                {
                    context.DataEdges.Add(new PendingDataEdge
                    {
                        SourceNodeId = producerNode.Id,
                        SourcePinName = producerPin.Name,
                        TargetNodeId = targetNode.Id,
                        TargetPinName = targetPinName,
                        PubVarName = pubVarName
                    });
                    return;
                }
            }
        }

        // Fallback: first-match-wins by PubVarName (legacy behaviour for when reaching-definitions
        // is unavailable or the reaching def's node is missing).
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
