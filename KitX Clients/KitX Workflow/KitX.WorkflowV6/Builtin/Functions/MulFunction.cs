namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// MulFunction — the integer multiplication builtin (discussion notes §十二-B:
// arithmetic operators disabled, replaced by builtins).
//
// KScript: <c>Mul(a, b)</c>.
// BP node: 2 data inputs (int, int), 1 data output (int).
// Codegen: <c>this.Mul(a, b)</c> → runtime <c>G.Mul(a, b) = a * b</c>.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Mul builtin — multiplies two integers. Pure: returns an Integer.
/// </summary>
public sealed class MulFunction : IBuiltinFunction
{
    public string Name => "Mul";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("A", PinType.Integer, 20),
        new("B", PinType.Integer, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Product", PinType.Integer, 50),
    ];
}
