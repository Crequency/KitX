namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The DictGetValues builtin — batch lookup, missing keys padded with null. Pure.
/// </summary>
public sealed class DictGetValuesFunction : BuiltinFunctionBase
{
    public DictGetValuesFunction() : base("DictGetValues", FunctionKind.Pure,
        [new("Dict", PinType.Dict, 20), new("Keys", PinType.Json, 35)],
        [new("Values", PinType.Json, 55)]) { }
}
