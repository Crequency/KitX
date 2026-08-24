namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The DictRemove builtin — removes a key in place, returns the same Dict. Pure
/// (data transform with an in-place side effect on the input reference).
/// </summary>
public sealed class DictRemoveFunction : BuiltinFunctionBase
{
    public DictRemoveFunction() : base("DictRemove", FunctionKind.Pure,
        [new("Dict", PinType.Dict, 20), new("Key", PinType.String, 35)],
        [new("Result", PinType.Dict, 50)]) { }
}
