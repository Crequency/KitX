namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The Mod builtin — modulo of two integers. Pure: returns an Integer.
/// </summary>
public sealed class ModFunction : BuiltinFunctionBase
{
    public ModFunction() : base("Mod", FunctionKind.Pure,
        [new("A", PinType.Integer, 20), new("B", PinType.Integer, 35)],
        [new("Remainder", PinType.Integer, 50)]) { }
}
