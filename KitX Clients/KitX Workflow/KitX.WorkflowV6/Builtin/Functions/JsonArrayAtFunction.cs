namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The JsonArrayAt builtin — gets an element at an index from a JSON array. Pure.
/// </summary>
public sealed class JsonArrayAtFunction : BuiltinFunctionBase
{
    public JsonArrayAtFunction() : base("JsonArrayAt", FunctionKind.Pure,
        [new("Value", PinType.Any, 20), new("Index", PinType.Integer, 35)],
        [new("Element", PinType.Json, 50)]) { }
}
