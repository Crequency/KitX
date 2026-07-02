namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrPipelineStatement — the v5.0 functional `>` / `=` syntax, carried verbatim.
//
// Replaces legacy PipelineStatement (which held a converter-injected Flattener
// delegate + a mutable cached flattening). Here the AST is pure immutable data;
// flattening is an external pure function in the lowering layer.
//
// A plain assignment `x = Func(args)` is a pipeline with one source (the call)
// and one Variable segment. A bare call `Func(args)` is a pipeline with one source
// and a single FunctionCall segment that has no terminal tap. The `>` multi-step
// form `a, b > F > G > x` has N sources and N segments.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A pipeline statement: one or more source expressions feeding an ordered chain
/// of segments. Carries the structured AST so BS round-trip is lossless and
/// .kcs can serialise the pipeline structure (not just flattened text).
/// </summary>
public sealed record IrPipelineStatement : IrStatement
{
    /// <summary>
    /// The verbatim source expressions (left side of the first `>`). Each entry is
    /// the original BS text of a source, e.g. <c>"Get(x)"</c>, <c>"42"</c>, <c>"a"</c>.
    /// </summary>
    public required ImmutableArray<string> Sources { get; init; }

    /// <summary>The ordered pipeline segments (each `> Target`).</summary>
    public required ImmutableArray<IrSegment> Segments { get; init; }
}
