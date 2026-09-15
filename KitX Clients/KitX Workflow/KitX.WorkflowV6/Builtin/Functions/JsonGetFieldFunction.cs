namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The JsonGetField builtin — gets a field value via dotted path from a JSON object. Pure.
/// </summary>
public sealed class JsonGetFieldFunction : BuiltinFunctionBase
{
    public JsonGetFieldFunction() : base("JsonGetField", FunctionKind.Pure,
        [new("Value", PinType.Any, 20), new("FieldPath", PinType.String, 35)],
        [new("Field", PinType.Json, 50)]) { }
}
