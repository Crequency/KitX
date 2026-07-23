namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// SubFunction — the integer subtraction builtin (discussion notes §十二-B:
// arithmetic operators disabled, replaced by builtins).
//
// KScript: <c>Sub(a, b)</c>.
// BP node: 2 data inputs (int, int), 1 data output (int).
// Codegen: <c>this.Sub(a, b)</c> → runtime <c>G.Sub(a, b) = a - b</c>.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Sub builtin — subtracts two integers. Pure: returns an Integer.
/// </summary>
public sealed class SubFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "Sub";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("A", PinType.Integer, 20),
        new("B", PinType.Integer, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Difference", PinType.Integer, 50),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(Ir.Statement stmt, CodeGenContext ctx)
    {
        // StructuredCodegen emits this.Sub(a, b) via the string-concatenation path.
        yield break;
    }
}
