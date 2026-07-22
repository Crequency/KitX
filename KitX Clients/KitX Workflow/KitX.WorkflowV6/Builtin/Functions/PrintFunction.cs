namespace KitX.WorkflowV6.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// PrintFunction — the canonical side-effect builtin (discussion notes §3.3 #9,
// §十二-K: pure/side-effect functions go through IBuiltinFunction, control flow
// does not).
//
// KScript: <c>Print(value)</c>.
// BP node: 1 data input (Any), no data output, Exec-in/Exec-out pins implicit.
// Codegen: <c>G.Print(args[0]);</c> — the value is passed to the runtime's Print
// dispatcher which routes to the dashboard's output panel.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The Print builtin — outputs a value to the console/dashboard. SideEffect: has a
/// data input but no consumed return value.
/// </summary>
public sealed class PrintFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "Print";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Value", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts => [];

    public IEnumerable<StatementSyntax> EmitCSharp(Ir.Statement stmt, CodeGenContext ctx)
    {
        // Print appears as a PipelineStatement with the value as a source (or as an
        // argument in a single call segment). Resolve the first source as the value.
        if (stmt is Ir.Statements.PipelineStatement p)
        {
            // The bare form is `Print(value)` parsed as a BsCall source with no segments;
            // the pipeline form is `value > Print` parsed with one source and one Print segment.
            // Either way, the value is the first source's structured content.
            // The Phase 4 codegen will translate the BsNode source into a Roslyn expression;
            // for now we emit G.Print(args[0]) when args are available.
            if (p.Segments.Length == 0)
            {
                // Bare call form: emit G.Print(<first-source-expression>)
                yield return ctx.GInvokeStatement("Print", ctx.ResolveArgument(p.Sources[0].SourceText));
                yield break;
            }
            // Pipeline form `value > Print`: emit G.Print(<first-source-expression>)
            yield return ctx.GInvokeStatement("Print", ctx.ResolveArgument(p.Sources[0].SourceText));
        }
    }
}