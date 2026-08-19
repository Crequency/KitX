namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The JsonToDict builtin — JsonElement → Dict bridge. Pure.
/// </summary>
public sealed class JsonToDictFunction : BuiltinFunctionBase
{
    public JsonToDictFunction() : base("JsonToDict", FunctionKind.Pure,
        [new("Json", PinType.Json, 20)],
        [new("Dict", PinType.Dict, 40)]) { }
}
