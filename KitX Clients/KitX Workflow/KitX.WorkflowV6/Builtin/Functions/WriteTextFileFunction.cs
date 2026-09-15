namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

/// <summary>
/// The WriteTextFile builtin — writes content to a text file (overwrites). SideEffect.
/// </summary>
public sealed class WriteTextFileFunction : BuiltinFunctionBase
{
    public WriteTextFileFunction() : base("WriteTextFile", FunctionKind.SideEffect,
        [new("Path", PinType.String, 20), new("Content", PinType.String, 35)],
        []) { }
}
