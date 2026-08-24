namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Sub builtin — subtracts two integers. Pure: returns an Integer.
/// </summary>
public sealed class SubFunction : BuiltinFunctionBase
{
    public SubFunction() : base("Sub", FunctionKind.Pure,
        [new("A", PinType.Integer, 20), new("B", PinType.Integer, 35)],
        [new("Difference", PinType.Integer, 50)]) { }
}
