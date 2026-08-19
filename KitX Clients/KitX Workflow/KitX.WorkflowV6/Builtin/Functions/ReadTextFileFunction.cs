namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The ReadTextFile builtin — reads a text file into a string. Pure (value-producing).
/// </summary>
public sealed class ReadTextFileFunction : BuiltinFunctionBase
{
    public ReadTextFileFunction() : base("ReadTextFile", FunctionKind.Pure,
        [new("Path", PinType.String, 20)],
        [new("Content", PinType.String, 50)]) { }
}
