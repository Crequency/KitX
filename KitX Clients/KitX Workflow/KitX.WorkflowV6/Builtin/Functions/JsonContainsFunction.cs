namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The JsonContains builtin — checks if a dotted path exists in a JSON object. Pure.
/// </summary>
public sealed class JsonContainsFunction : BuiltinFunctionBase
{
    public JsonContainsFunction() : base("JsonContains", FunctionKind.Pure,
        [new("Value", PinType.Any, 20), new("Path", PinType.String, 35)],
        [new("Found", PinType.Boolean, 50)]) { }
}
