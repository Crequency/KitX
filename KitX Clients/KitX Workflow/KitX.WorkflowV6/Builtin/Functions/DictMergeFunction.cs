namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The DictMerge builtin — merges source into target in place, returns target. Pure
/// (data transform with an in-place side effect on the target reference).
/// </summary>
public sealed class DictMergeFunction : BuiltinFunctionBase
{
    public DictMergeFunction() : base("DictMerge", FunctionKind.Pure,
        [new("Target", PinType.Dict, 20), new("Source", PinType.Dict, 40)],
        [new("Result", PinType.Dict, 60)]) { }
}
