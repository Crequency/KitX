namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Compare builtin — compares two values with a named operator. Pure: returns a
/// Boolean. Replaces the disabled comparison operators per §十二-B.
/// </summary>
public sealed class CompareFunction : BuiltinFunctionBase
{
    public CompareFunction() : base("Compare", FunctionKind.Pure,
        [new("Op", PinType.String, 20), new("A", PinType.Any, 35), new("B", PinType.Any, 50)],
        [new("Result", PinType.Boolean, 50)]) { }
}
