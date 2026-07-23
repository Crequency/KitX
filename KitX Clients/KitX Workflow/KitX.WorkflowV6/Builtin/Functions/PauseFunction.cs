namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// PauseFunction — the sleep/delay builtin. Ported from v5.1 SimpleBuiltinFunctions.
//
// KScript: <c>Pause(milliseconds)</c>.
// BP node: 1 data input (int), no data output.
// Codegen: <c>this.Pause(ms)</c> → runtime <c>G.Pause(ms)</c> → Thread.Sleep.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Pause builtin — suspends execution for N milliseconds. SideEffect.
/// </summary>
public sealed class PauseFunction : IBuiltinFunction
{
    public string Name => "Pause";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Milliseconds", PinType.Integer, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];
}
