namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// AddFunction — the integer addition builtin (discussion notes §十二-B:
// arithmetic operators disabled, replaced by builtins).
//
// KScript: <c>Add(a, b)</c>.
// BP node: 2 data inputs (int, int), 1 data output (int).
// Codegen: <c>(a + b)</c> once types are known (strong-typed per §十二-F), or
// <c>G.Add(a, b)</c> when dynamic.
//
// Replaces the disabled `+` operator: every addition is a function call node, so the
// KS↔BP 1:1 mapping stays exact (§十二-B).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Add builtin — adds two integers. Pure: returns an Integer. Replaces
/// the disabled `+` operator per §十二-B.
/// </summary>
public sealed class AddFunction : IBuiltinFunction
{
    public string Name => "Add";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("A", PinType.Integer, 20),
        new("B", PinType.Integer, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Sum", PinType.Integer, 50),
    ];
}