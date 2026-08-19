namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The DictContainsKey builtin — key existence check. Pure.
/// </summary>
public sealed class DictContainsKeyFunction : BuiltinFunctionBase
{
    public DictContainsKeyFunction() : base("DictContainsKey", FunctionKind.Pure,
        [new("Dict", PinType.Dict, 20), new("Key", PinType.String, 35)],
        [new("Found", PinType.Boolean, 50)]) { }
}
