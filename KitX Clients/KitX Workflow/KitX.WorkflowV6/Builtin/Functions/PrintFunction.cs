namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Print builtin — outputs a value to the console/dashboard. SideEffect: has a
/// data input but no consumed return value.
/// </summary>
public sealed class PrintFunction : BuiltinFunctionBase
{
    public PrintFunction() : base("Print", FunctionKind.SideEffect,
        [new("Value", PinType.Any, 35)],
        []) { }
}
