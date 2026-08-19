namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The JsonAsString builtin — extracts a string from a JSON value. Pure.
/// </summary>
public sealed class JsonAsStringFunction : BuiltinFunctionBase
{
    public JsonAsStringFunction() : base("JsonAsString", FunctionKind.Pure,
        [new("Value", PinType.Any, 20)],
        [new("Result", PinType.String, 50)]) { }
}
