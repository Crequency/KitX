namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Pause builtin — suspends execution for N milliseconds. SideEffect.
/// </summary>
public sealed class PauseFunction : BuiltinFunctionBase
{
    public PauseFunction() : base("Pause", FunctionKind.SideEffect,
        [new("Milliseconds", PinType.Integer, 20)],
        []) { }
}
