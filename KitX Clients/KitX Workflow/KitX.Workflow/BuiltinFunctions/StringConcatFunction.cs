using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions;

/// <summary>
/// StringConcat builtin function â€?concatenates N string inputs into one.
/// BlockScript syntax: StringConcat(part1, part2, ...)
/// Initial blueprint node has 2 input pins; the editor auto-expands a new pin when the
/// last one is connected (see BlueprintEditorViewModel.Connect).
/// </summary>
public class StringConcatFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "StringConcat";
    public string DisplayName => "String Concat";
    public bool IsNonExtractable => false; // value-producing, can be nested

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
    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
    {
        // Generate G.StringConcat(arg1, arg2, ...) â€?argument count is dynamic.
        var args = (stmt.Arguments ?? new List<string>())
            .Select(a => ctx.ResolveArgument(a))
            .ToArray();
        var concatExpr = ctx.GInvoke("StringConcat", args);
        return ctx.EmitValueAssignment(stmt.PubVarTarget, concatExpr);
    }
}