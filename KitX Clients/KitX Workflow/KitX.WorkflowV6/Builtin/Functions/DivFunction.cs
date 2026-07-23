namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// DivFunction — the integer division builtin (discussion notes §十二-B:
// arithmetic operators disabled, replaced by builtins).
//
// KScript: <c>Div(a, b)</c>.
// BP node: 2 data inputs (int, int), 1 data output (int).
// Codegen: <c>this.Div(a, b)</c> → runtime <c>G.Div(a, b) = a / b</c> (integer
// division). Throws DivideByZeroException when b == 0, matching C# semantics.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Div builtin — integer division of two integers. Pure: returns an Integer.
/// </summary>
public sealed class DivFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "Div";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("A", PinType.Integer, 20),
        new("B", PinType.Integer, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Quotient", PinType.Integer, 50),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(Ir.Statement stmt, CodeGenContext ctx)
    {
        yield break;
    }
}
