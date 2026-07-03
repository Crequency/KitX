namespace KitX.WorkflowIR.Builtin;

using KitX.WorkflowIR.Ir;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// Shared helpers used by builtin function descriptors when emitting C#.
//
// The legacy emitters read flat fields off CFGStatement (Arguments / PubVarTarget /
// TrueBlockName). The new IR is split into IrPipelineStatement (value calls) and
// IrControlFlowStatement (terminators) with no shared flat-argument surface, so a
// small set of projection helpers keep each descriptor's EmitCSharp readable
// without duplicating the IrPipeline/IrControlFlow dispatch in every function.
//
// These are deliberately thin: they extract the pre-flattened argument strings and
// the assignment target, leaving all Roslyn construction to CodeGenContext.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Projections over <see cref="IrStatement"/> that the builtin descriptors share.
/// Keeps the value-call (IrPipeline) vs control-flow (IrControlFlow) dispatch in
/// one place instead of repeating it across every descriptor's EmitCSharp.
/// </summary>
internal static class BuiltinEmitHelpers
{
    /// <summary>
    /// The flat argument strings for a statement. For a value call these are the
    /// terminal segment's literal/placeholder arguments; for control flow they are
    /// <see cref="IrControlFlowStatement.Arguments"/>. Empty when not applicable.
    /// </summary>
    public static IReadOnlyList<string> FlatArguments(IrStatement stmt)
    {
        if (stmt is IrPipelineStatement pipe && pipe.Segments.Length > 0)
        {
            var last = pipe.Segments[^1];
            if (last.Kind == IrSegmentKind.FunctionCall)
                return last.Arguments
                    .Select(a => a.Kind == IrPipelineArgumentKind.Literal ? (a.Literal ?? "") : "")
                    .Where(s => s.Length > 0)
                    .ToList();
        }
        if (stmt is IrControlFlowStatement cf)
            return cf.Arguments.ToList();
        return [];
    }

    /// <summary>
    /// The PubVar target the result should bind to. For a value call this is the
    /// terminal variable-tap segment's <see cref="IrSegment.VariableName"/>; for
    /// control flow it is null (control-flow terminators produce no value).
    /// </summary>
    public static string? AssignedVariable(IrStatement stmt)
    {
        if (stmt is IrPipelineStatement pipe && pipe.Segments.Length > 0)
        {
            var last = pipe.Segments[^1];
            if (last.Kind == IrSegmentKind.Variable)
                return last.VariableName;
        }
        return null;
    }

    /// <summary>
    /// Resolves every flat argument of <paramref name="stmt"/> into Roslyn
    /// expressions via <paramref name="ctx"/>. The common shape for the value builtin
    /// emitters (Json*, StringConcat, Print, Pause): G.&lt;Name&gt;(arg0, arg1, ...).
    /// </summary>
    public static ExpressionSyntax[] ResolveArguments(IrStatement stmt, CodeGenContext ctx)
        => FlatArguments(stmt).Select(a => ctx.ResolveArgument(a)).ToArray();
}
