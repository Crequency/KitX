namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Range builtin — produces an integer range <c>[from, to)</c> with the given step.
/// Pure: returns a value, no side effects. Used as the canonical forEach source.
/// </summary>
public sealed class RangeFunction : BuiltinFunctionBase
{
    public RangeFunction() : base("Range", FunctionKind.Pure,
        [new("From", PinType.Integer, 20), new("To", PinType.Integer, 35), new("Step", PinType.Integer, 50)],
        [new("Range", PinType.Json, 50)]) { }
}
