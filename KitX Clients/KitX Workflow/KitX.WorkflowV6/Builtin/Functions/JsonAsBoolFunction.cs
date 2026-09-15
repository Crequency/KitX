namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The JsonAsBool builtin — extracts a boolean from a JSON value. Pure.
/// </summary>
public sealed class JsonAsBoolFunction : BuiltinFunctionBase
{
    public JsonAsBoolFunction() : base("JsonAsBool", FunctionKind.Pure,
        [new("Value", PinType.Any, 20)],
        [new("Result", PinType.Boolean, 50)]) { }
}
