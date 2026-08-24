namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The DictKeys builtin — all keys as a JSON array. Pure.
/// </summary>
public sealed class DictKeysFunction : BuiltinFunctionBase
{
    public DictKeysFunction() : base("DictKeys", FunctionKind.Pure,
        [new("Dict", PinType.Dict, 20)],
        [new("Keys", PinType.Json, 40)]) { }
}
