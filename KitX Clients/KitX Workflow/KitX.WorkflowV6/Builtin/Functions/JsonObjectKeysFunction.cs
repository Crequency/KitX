namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The JsonObjectKeys builtin — gets key names of a JSON object. Pure.
/// </summary>
public sealed class JsonObjectKeysFunction : BuiltinFunctionBase
{
    public JsonObjectKeysFunction() : base("JsonObjectKeys", FunctionKind.Pure,
        [new("Value", PinType.Any, 20)],
        [new("Keys", PinType.Json, 50)]) { }
}
