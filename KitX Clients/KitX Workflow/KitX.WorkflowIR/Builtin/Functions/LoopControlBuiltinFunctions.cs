namespace KitX.WorkflowIR.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Builtin;
using KitX.WorkflowIR.Ir;
using KitX.WorkflowIR.Ir.Ast;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// Break + Flip — two further control-flow builtins.
//
// Break: exits the enclosing loop. Targets is empty (the loop machinery consumes
// it). The legacy library had no standalone Break class — Exit was the v5.0 rename
// of the former Break — but the new IR's ControlFlowOp keeps Break distinct from
// Exit because "exit loop" and "exit script" are different runtime semantics.
//
// Flip: alternating two-way control flow (A on odd calls, B on even). The legacy
// descriptor carried an obsolete ExtractStatement override (the v5.1 cleanup removed
// that API surface) and a static runtime counter. Here the descriptor is clean;
// the per-activation counter becomes an instance field in the Phase 7 runtime.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Break builtin — exit the enclosing loop. Targets is empty (handled by the loop).
/// BlockScript: <c>Break()</c>.
/// </summary>
public sealed class BreakFunction : IBuiltinFunction, IParserHandler, ICodeGenHandler
{
    public string Name => "Break";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts => [new("Exec", PinType.Execution, 20)];
    public IReadOnlyList<PortSpec> OutputPorts => [];

    public FlowControlStatement ParseInvocation(BSCall call, int sourceLine) => new()
    {
        LineNumber = sourceLine,
        SourceCode = call.SourceText,
        FunctionName = "Break",
    };

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        // break; — exits the generated switch block that encloses the loop body case.
        yield return ctx.Return();
    }
}

/// <summary>
/// Flip builtin — alternating control flow. Each execution alternates between the
/// two output arms (A on odd activations, B on even), cycling repeatedly.
/// BlockScript: <c>Flip("blockA", "blockB")</c>.
/// </summary>
/// <remarks>
/// The runtime counter (the legacy static <c>_flipCounter</c>) is deferred to Phase 7,
/// where it becomes a per-execution instance field instead of process-wide static
/// state — fixing the latent bug that two concurrent scripts would share the counter.
/// </remarks>
public sealed class FlipFunction : IBuiltinFunction, IParserHandler, ICodeGenHandler
{
    public string Name => "Flip";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts => [new("Exec", PinType.Execution, 30)];

    // Two Exec arms (A/B) are structural control edges implied by the Flip op —
    // OutputPorts empty per v5.0 §7.
    public IReadOnlyList<PortSpec> OutputPorts => [];

    public FlowControlStatement ParseInvocation(BSCall call, int sourceLine)
    {
        var args = call.Args;
        var blockA = args.ElementAtOrDefault(0)?.AsStringLiteral() ?? "";
        var blockB = args.ElementAtOrDefault(1)?.AsStringLiteral() ?? "";
        return new FlowControlStatement
        {
            LineNumber = sourceLine,
            SourceCode = call.SourceText,
            FunctionName = "Flip",
            FlowArguments = [blockA, blockB],
            Arms =
            [
                new() { PinName = "A", TargetBlockName = blockA },
                new() { PinName = "B", TargetBlockName = blockB },
            ],
        };
    }

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        // Flip shares the Branch control-flow form: G.NextBlock = G.Flip(a, b); break;
        var cf = (IrControlFlowStatement)stmt;
        var blockA = cf.Targets.Length > 0 ? cf.Targets[0].TargetBlockName : "";
        var blockB = cf.Targets.Length > 1 ? cf.Targets[1].TargetBlockName : "";
        foreach (var s in ctx.EmitNextBlockAssignment("Flip", ctx.Literal(blockA), ctx.Literal(blockB)))
            yield return s;
    }
}
