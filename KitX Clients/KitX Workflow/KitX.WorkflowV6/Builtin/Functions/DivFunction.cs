namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Div builtin — integer division of two integers. Pure: returns an Integer.
/// </summary>
public sealed class DivFunction : BuiltinFunctionBase
{
    public DivFunction() : base("Div", FunctionKind.Pure,
        [new("A", PinType.Integer, 20), new("B", PinType.Integer, 35)],
        [new("Quotient", PinType.Integer, 50)]) { }
}
