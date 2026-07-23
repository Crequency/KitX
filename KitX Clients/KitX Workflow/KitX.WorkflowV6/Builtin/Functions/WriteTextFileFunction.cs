namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// WriteTextFileFunction — writes content to a text file (overwrites). Ported from v5.1.
//
// KScript: <c>WriteTextFile(path, content)</c>.
// BP node: 2 data inputs (string path, string content), no data output.
// Codegen: <c>this.WriteTextFile(path, content)</c> → runtime File.WriteAllText.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The WriteTextFile builtin — writes content to a text file (overwrites). SideEffect.
/// </summary>
public sealed class WriteTextFileFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "WriteTextFile";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Path", PinType.String, 20),
        new("Content", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];

    public IEnumerable<StatementSyntax> EmitCSharp(Ir.Statement stmt, CodeGenContext ctx)
    {
        yield break;
    }
}
