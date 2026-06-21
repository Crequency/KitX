using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.Models;
using static KitX.Workflow.BlockScripting.BlockScriptWellKnown.Pins;

namespace KitX.Workflow.Blueprint;

/// <summary>
/// Implements INodeExportHelper — resolves input values for export strategies
/// by tracing data connections, PubVar assignments, and const references.
/// Extracted from BlueprintToBlockScriptConverter for single-responsibility.
/// </summary>
internal class NodeExportHelper : INodeExportHelper
{
    /// <inheritdoc/>
    public KitX.Core.Contract.Workflow.Blueprint Blueprint { get; private set; } = null!;

    /// <summary>Sets the current blueprint and context for resolution</summary>
    public void SetContext(KitX.Core.Contract.Workflow.Blueprint blueprint, ConversionContext? ctx)
    {
        Blueprint = blueprint;
        _currentCtx = ctx;
    }

    private ConversionContext? _currentCtx;

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
            if (pin.Name != Exec)
                args.Add(GetInputValue(node, pin.Name));
        }
        return string.Join(", ", args);
    }

    /// <inheritdoc/>
    public string? GetOutputPubVar(BlueprintNode node, string pinName)
        => _currentCtx != null ? FindOutputPubVar(node, pinName, _currentCtx) : null;

    /// <inheritdoc/>
    public bool IsOutputConsumed(BlueprintNode node, string pinName)
        => _currentCtx?.ConsumedOutputs.Contains((node.Id, pinName)) == true;

    /// <summary>
    /// Finds the PubVar name assigned to a node's output pin.
    /// </summary>
    public static string? FindOutputPubVar(BlueprintNode node, string pinName, ConversionContext ctx)
    {
        var pin = node.OutputPins.FirstOrDefault(p => p.Name == pinName);
        if (pin == null) return null;
        var conn = ctx.DataConnections.FirstOrDefault(c => c.SourcePinId == pin.Id);
        return conn?.PubVarName;
    }

    /// <summary>
    /// Formats a literal value for BlockScript output — wraps strings in quotes,
    /// passes through numbers, booleans, char literals, and already-quoted values.
    /// Uses Roslyn to validate character literals rather than string-pattern heuristics.
    /// </summary>
    public static string FormatLiteralValue(string value, ConversionContext? ctx = null)
    {
        if (value == null) return string.Empty;
        if (value.Length == 0) return "\"\"";  // empty string literal
        if (value.StartsWith("\"")) return value;
        if (BSExpressionExtensions.IsCharacterLiteral(value)) return value;  // char literal — pass through
        if (ctx != null && ctx.AllPubVars.Contains(value)) return value;
        if (int.TryParse(value, out _) || double.TryParse(value, out _)) return value;
        if (value == "true" || value == "false") return value;
        if (value.Contains("(")) return value;
        if (ctx != null && ctx.Blueprint.Nodes.OfType<ConstNode>().Any(c => c.ConstName == value))
            return value;
        return $"\"{value}\"";
    }
}
