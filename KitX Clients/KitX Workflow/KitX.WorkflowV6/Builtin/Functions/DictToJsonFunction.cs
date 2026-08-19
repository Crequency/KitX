namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The DictToJson builtin — Dict → JsonElement bridge. Pure.
/// </summary>
public sealed class DictToJsonFunction : BuiltinFunctionBase
{
    public DictToJsonFunction() : base("DictToJson", FunctionKind.Pure,
        [new("Dict", PinType.Dict, 20)],
        [new("Json", PinType.Json, 40)]) { }
}
