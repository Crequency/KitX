namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// ModFunction — the integer modulo builtin (discussion notes §十二-B:
// arithmetic operators disabled, replaced by builtins).
//
// KScript: <c>Mod(a, b)</c>.
// BP node: 2 data inputs (int, int), 1 data output (int).
// Codegen: <c>this.Mod(a, b)</c> → runtime <c>G.Mod(a, b) = a % b</c>.
// Throws DivideByZeroException when b == 0, matching C# semantics.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Mod builtin — modulo of two integers. Pure: returns an Integer.
/// </summary>
public sealed class ModFunction : IBuiltinFunction
{
    public string Name => "Mod";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("A", PinType.Integer, 20),
        new("B", PinType.Integer, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Remainder", PinType.Integer, 50),
    ];
}
