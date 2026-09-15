namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The JsonAsInt builtin — extracts an integer from a JSON value. Pure.
/// </summary>
public sealed class JsonAsIntFunction : BuiltinFunctionBase
{
    public JsonAsIntFunction() : base("JsonAsInt", FunctionKind.Pure,
        [new("Value", PinType.Any, 20)],
        [new("Result", PinType.Integer, 50)]) { }
}
