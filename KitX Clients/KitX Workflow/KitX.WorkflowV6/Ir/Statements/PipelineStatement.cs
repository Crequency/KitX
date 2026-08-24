namespace KitX.WorkflowV6.Ir.Statements;

using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// PipelineStatement — the v5 functional `>` / `=` data-flow syntax, carried as a
// structured AST (not raw text). Ported from archived v5.1 KitX.WorkflowIR.IrPipelineStatement.
//
// The pipeline is the *only* data-flow construct in v6 (same as v5 §1). Plain
// assignment `x = Func(args)` is a pipeline with one source and one variable-tap
// segment; a bare call `Func(args)` is a pipeline with one source and a single
// function-call segment that has no terminal tap; the multi-step form
// `a, b > F > G > x` has N sources and N segments.
//
// Control flow is NOT expressed via pipelines (see IfStatement / ForEachStatement /
// etc.). Pipelines are pure data transforms and live *inside* control-flow bodies.
//
// v6 refinement over v5: <see cref="Sources"/> and <see cref="Segment.Arguments"/>
// are now <see cref="KsNode"/> trees (not raw strings). This makes fingerprinting,
// diffing, serialisation, and BP rendering all operate on structured content — so
// re-parsing the same KS text produces the same fingerprint, and whitespace-only
// drift never changes identity.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A pipeline statement: one or more source expressions feeding an ordered chain of
/// segments. Carries the structured AST so KS round-trip is lossless and the file
/// format can serialise pipeline structure (not just flattened text).
/// </summary>
public sealed record PipelineStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <inheritdoc/>
    public override KitX.WorkflowV6.Ir.StatementKind Kind =>
        KitX.WorkflowV6.Ir.StatementKind.Pipeline;

    /// <summary>
    /// The structured source expressions (left side of the first <c>&gt;</c>). Each entry
    /// is a <see cref="KsNode"/> — typically a <see cref="KsLiteral"/>, <see cref="KsIdentifier"/>,
    /// or a nested <see cref="KsCall"/>. Replaces the v5 raw-string form.
    /// </summary>
    public required ImmutableArray<KsNode> Sources { get; init; }

    /// <summary>The ordered pipeline segments (each <c>&gt; Target</c>).</summary>
    public required ImmutableArray<Segment> Segments { get; init; }

    public bool Equals(PipelineStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (LeadingComment != other.LeadingComment) return false;
        if (TrailingComment != other.TrailingComment) return false;
        if (Sources.Length != other.Sources.Length) return false;
        for (int i = 0; i < Sources.Length; i++)
            if (!Sources[i].Equals(other.Sources[i])) return false;
        if (Segments.Length != other.Segments.Length) return false;
        for (int i = 0; i < Segments.Length; i++)
            if (!Segments[i].Equals(other.Segments[i])) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(LeadingComment);
        hash.Add(TrailingComment);
                foreach (var s in Sources) hash.Add(s);
        foreach (var s in Segments) hash.Add(s);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A single segment of a pipeline (one <c>&gt; Target</c>). Either a function call
/// (with optional arguments, which may include <see cref="KsPlaceholder"/>s for
/// pipeline-value insertion) or a variable tap (<c>&gt; x</c> with no arguments).
/// </summary>
/// <remarks>
/// The v6 shape uses <see cref="KsNode"/> for <see cref="Arguments"/> (not raw strings)
/// so the structured fingerprint, diff, and serialiser all operate on the AST. (The
/// v5.1 raw-text <c>RawArguments</c> cache was removed in W-5 — all lowering paths
/// consume the structured AST.)
/// </remarks>
public sealed record Segment
{
    /// <summary>The function name (e.g. "Print", "Range") or the variable tap name.</summary>
    public required string Target { get; init; }

    /// <summary>
    /// Structured argument expressions for a call segment. Empty for a variable tap.
    /// May contain <see cref="KsPlaceholder"/> nodes marking pipeline-value insertion slots.
    /// </summary>
    public ImmutableArray<KsNode> Arguments { get; init; } = [];

    /// <summary>True when this segment is a variable assignment tap rather than a call.</summary>
    public bool IsVariableTap { get; init; }

    /// <summary>
    /// Inline comment on this segment's line in a multi-line pipeline
    /// (<c>    &gt; Func // cmt</c>). Null in single-line pipelines. Forces multi-line
    /// rendering when non-null. Maps to this segment's function node
    /// <c>BlueprintNode.Comment</c>.
    /// </summary>
    public string? Comment { get; init; }

    public bool Equals(Segment? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Target != other.Target) return false;
        // IsVariableTap deliberately NOT compared: it is a derived flag (a segment is
        // a var tap iff Arguments.Length == 0 AND Target is not a known function). The
        // forward path (Parser) and reverse path (BpReverseTranslator) currently set it
        // asymmetrically for the `> name` form (Parser: false, Reverse: true) because
        // the BP graph doesn't preserve the syntactic distinction between `> name` and
        // `= name`. Excluding it from equality keeps round-trip stable while preserving
        // the flag for codegen consumers that read it directly.
        if (Comment != other.Comment) return false;
        if (Arguments.Length != other.Arguments.Length) return false;
        for (int i = 0; i < Arguments.Length; i++)
            if (!Arguments[i].Equals(other.Arguments[i])) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Target);
        // IsVariableTap NOT hashed (see Equals rationale).
        hash.Add(Comment);
        foreach (var a in Arguments) hash.Add(a);
        return hash.ToHashCode();
    }
}