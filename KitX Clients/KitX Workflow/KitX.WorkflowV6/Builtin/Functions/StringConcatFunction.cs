namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The StringConcat builtin — concatenates N string arguments into one. Pure: returns
/// a value. Declares a variadic input spec so the BP editor auto-grows new string pins
/// when the last one is connected.
/// </summary>
public sealed class StringConcatFunction : BuiltinFunctionBase
{
    public StringConcatFunction() : base("StringConcat", FunctionKind.Pure,
        [new("A", PinType.String, 20), new("B", PinType.String, 35)],
        [new("Concat", PinType.String, 50)],
        new("Input ", 3, PinType.String)) { }
}
