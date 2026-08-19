namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The DictGetValue builtin — gets a value by key from a Dict. Pure.
/// </summary>
public sealed class DictGetValueFunction : BuiltinFunctionBase
{
    public DictGetValueFunction() : base("DictGetValue", FunctionKind.Pure,
        [new("Dict", PinType.Dict, 20), new("Key", PinType.String, 35)],
        [new("Value", PinType.Any, 50)]) { }
}
