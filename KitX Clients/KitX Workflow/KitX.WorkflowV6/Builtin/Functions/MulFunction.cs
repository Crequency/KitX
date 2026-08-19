namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Mul builtin — multiplies two integers. Pure: returns an Integer.
/// </summary>
public sealed class MulFunction : BuiltinFunctionBase
{
    public MulFunction() : base("Mul", FunctionKind.Pure,
        [new("A", PinType.Integer, 20), new("B", PinType.Integer, 35)],
        [new("Product", PinType.Integer, 50)]) { }
}
