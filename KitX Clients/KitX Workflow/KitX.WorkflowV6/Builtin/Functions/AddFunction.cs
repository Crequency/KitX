namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Add builtin — adds two integers. Pure: returns an Integer. Replaces the
/// disabled `+` operator per §十二-B.
/// </summary>
public sealed class AddFunction : BuiltinFunctionBase
{
    public AddFunction() : base("Add", FunctionKind.Pure,
        [new("A", PinType.Integer, 20), new("B", PinType.Integer, 35)],
        [new("Sum", PinType.Integer, 50)]) { }
}
