namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// ReadTextFileFunction — reads a text file into a string. Ported from v5.1.
//
// KScript: <c>ReadTextFile(path)</c>.
// BP node: 1 data input (string path), 1 data output (string content).
// Codegen: <c>this.ReadTextFile(path)</c> → runtime File.ReadAllText.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The ReadTextFile builtin — reads a text file into a string. Pure (value-producing).
/// </summary>
public sealed class ReadTextFileFunction : IBuiltinFunction
{
    public string Name => "ReadTextFile";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Path", PinType.String, 20),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Content", PinType.String, 50),
    ];
}
