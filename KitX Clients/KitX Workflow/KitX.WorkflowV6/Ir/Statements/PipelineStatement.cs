namespace KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// PipelineStatement — the v5 functional `>` / `=` data-flow syntax, carried
// verbatim as a structured AST. Inherited from KitX.WorkflowIR.IrPipelineStatement.
//
// The pipeline is the *only* data-flow construct in v6 (same as v5 §1). Plain
// assignment `x = Func(args)` is a pipeline with one source and one Variable
// segment; a bare call `Func(args)` is a pipeline with one source and a single
// FunctionCall segment that has no terminal tap; the multi-step form
// `a, b > F > G > x` has N sources and N segments.
//
// Control flow is NOT expressed via pipelines (see IfStatement / ForEachStatement /
// etc.). Pipelines are pure data transforms and live *inside* control-flow bodies.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A pipeline statement: one or more source expressions feeding an ordered chain
/// of segments. Carries the structured AST so BS round-trip is lossless and the
/// file format can serialise pipeline structure (not just flattened text).
/// </summary>
public sealed record PipelineStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <summary>
    /// The verbatim source expressions (left side of the first <c>&gt;</c>). Each
    /// entry is the original BS text of a source, e.g. <c>"Get(x)"</c>, <c>"42"</c>,
    /// <c>"a"</c>. The structured AST shape will be refined during the implementation
    /// phase; until then the raw textual form preserves round-trip fidelity.
    /// </summary>
    public required ImmutableArray<string> Sources { get; init; }

    /// <summary>The ordered pipeline segments (each <c>&gt; Target</c>).</summary>
    public required ImmutableArray<Segment> Segments { get; init; }

    public bool Equals(PipelineStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Fingerprint.Equals(other.Fingerprint)
            && Comment == other.Comment
            && SourceLine == other.SourceLine
            && Sources.SequenceEqual(other.Sources)
            && Segments.SequenceEqual(other.Segments);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(Comment);
        hash.Add(SourceLine);
        foreach (var s in Sources) hash.Add(s);
        foreach (var s in Segments) hash.Add(s);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A single segment of a pipeline (one <c>&gt; Target</c>). Carried as a structural
/// placeholder pending the AST refinement. The shape mirrors
/// <c>KitX.WorkflowIR.IrSegment</c> at a high level: a function call (or a variable
/// tap), with optional argument list.
/// </summary>
public sealed record Segment
{
    /// <summary>The function name (e.g. "Print", "Range") or the variable tap name.</summary>
    public required string Target { get; init; }

    /// <summary>Raw argument strings (post nested-call expansion).</summary>
    public ImmutableArray<string> Arguments { get; init; } = [];

    /// <summary>True when this segment is a variable assignment tap rather than a call.</summary>
    public bool IsVariableTap { get; init; }

    public bool Equals(Segment? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Target == other.Target
            && IsVariableTap == other.IsVariableTap
            && Arguments.SequenceEqual(other.Arguments);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Target);
        hash.Add(IsVariableTap);
        foreach (var a in Arguments) hash.Add(a);
        return hash.ToHashCode();
    }
}
