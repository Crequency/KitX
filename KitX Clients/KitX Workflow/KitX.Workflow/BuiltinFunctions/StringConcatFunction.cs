using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.BuiltinFunctions;

/// <summary>
/// StringConcat builtin function — concatenates N string inputs into one.
/// BlockScript syntax: StringConcat(part1, part2, ...)
/// Initial blueprint node has 2 input pins; the editor auto-expands a new pin when the
/// last one is connected (see BlueprintEditorViewModel.Connect).
/// </summary>
public class StringConcatFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "StringConcat";
    public string DisplayName => "String Concat";
    public bool IsFlowControl => false;
    public bool IsNonExtractable => false; // value-producing, can be nested
    public CFGStatementKind StatementKind => CFGStatementKind.Expression;
    public double NodeWidth => 140;
    public double NodeHeight => 70;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 20),
        new("A", PinType.String, 35),
        new("B", PinType.String, 55),
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [
        new("Exec", PinType.Execution, 20),
        new("Result", PinType.String, 40),
    ];

    /// <summary>
    /// Input-side variadic growth: when the last String input is connected, the editor
    /// auto-appends a new "Input {N}" String pin. Declared here (on the descriptor) so the
    /// editor's generic variadic logic handles it instead of the former StringConcat name match.
    /// </summary>
    public VariadicPinSpec? InputVariadic => new("Input ", 3, PinType.String);

    public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
    {
        // Generate G.StringConcat(arg1, arg2, ...) — argument count is dynamic.
        var args = (stmt.Arguments ?? new List<string>())
            .Select(a => ctx.ResolveArgument(a))
            .ToArray();
        var concatExpr = ctx.GInvoke("StringConcat", args);
        return ctx.EmitValueAssignment(stmt.PubVarTarget, concatExpr);
    }

    public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
    {
        // Dynamically add String input pins to match the argument count.
        // The static InputPins declares 2; if the statement has more, append extras.
        var existingDataPins = node.InputPins.Count(p => p.Type != PinType.Execution);
        var needed = stmt.Arguments?.Count ?? 0;
        for (int i = existingDataPins; i < needed; i++)
        {
            node.InputPins.Add(new BlueprintPin
            {
                Name = $"Input {i + 1}",
                Direction = PinDirection.Input,
                Type = PinType.String
            });
        }
        return node;
    }

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        // Collect all non-Exec input pin values in order → StringConcat(val1, val2, ...)
        var parts = node.InputPins
            .Where(p => p.Type != PinType.Execution)
            .Select(p => helper.GetInputValue(node, p.Name))
            .ToList();
        if (parts.Count < 2) return null;

        var expr = $"{FunctionName}({string.Join(", ", parts)})";

        // If the Result output pin is consumed by a downstream data edge, emit as an
        // assignment to the PubVar on that edge; otherwise emit as a bare expression.
        var pubVar = helper.GetOutputPubVar(node, "Result");
        if (!string.IsNullOrEmpty(pubVar))
        {
            return new ExpressionStatement
            {
                Expression = expr,
                SourceCode = $"{pubVar} = {expr};",
                LineNumber = 1
            };
        }

        return new ExpressionStatement
        {
            Expression = expr,
            SourceCode = expr + ";",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}