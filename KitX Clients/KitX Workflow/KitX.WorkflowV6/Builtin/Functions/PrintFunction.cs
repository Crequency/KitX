namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// PrintFunction — the canonical side-effect builtin (discussion notes §3.3 #9,
// §十二-K: pure/side-effect functions go through IBuiltinFunction, control flow
// does not).
//
// KScript: <c>Print(value)</c>.
// BP node: 1 data input (Any), no data output, Exec-in/Exec-out pins implicit.
// Codegen: <c>G.Print(args[0]);</c> — the value is passed to the runtime's Print
// dispatcher which routes to the dashboard's output panel.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Print builtin — outputs a value to the console/dashboard. SideEffect: has a
/// data input but no consumed return value.
/// </summary>
public sealed class PrintFunction : IBuiltinFunction
{
    public string Name => "Print";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}