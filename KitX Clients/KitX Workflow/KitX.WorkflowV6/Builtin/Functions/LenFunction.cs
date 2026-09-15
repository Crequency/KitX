namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Len builtin — returns the length/count of a value. Pure: returns an Integer.
/// Accepts strings, JSON arrays/objects, and .NET arrays/collections.
/// </summary>
public sealed class LenFunction : BuiltinFunctionBase
{
    public LenFunction() : base("Len", FunctionKind.Pure,
        [new("Value", PinType.Any, 20)],
        [new("Length", PinType.Integer, 50)]) { }
}
