namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The DictSetValue builtin — sets key→value in place, returns the same Dict. Pure
/// (data transform with an in-place side effect on the input reference).
/// </summary>
public sealed class DictSetValueFunction : BuiltinFunctionBase
{
    public DictSetValueFunction() : base("DictSetValue", FunctionKind.Pure,
        [new("Dict", PinType.Dict, 20), new("Key", PinType.String, 35), new("Value", PinType.Any, 50)],
        [new("Result", PinType.Dict, 70)]) { }
}
