namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrSegment — one atomic segment of a pipeline AST.
//
// In the legacy model, PipelineStatement was a first-class CFG citizen that held
// a BSPipeline AST *and* a Flattener delegate injected by the converter, plus a
// mutable cached flattening. That couple the data model to the lowering algorithm
// and made it stateful.
//
// IrSegment is pure data. A pipeline statement (IrPipelineStatement) is just an
// immutable list of sources + list of segments. Flattening becomes an external
// pure function (see BsLowerer) that takes (IrPipelineStatement, context) and
// returns the imperative statement sequence — no delegates, no caches, no
// coupling. The AST survives .kcs round-trip verbatim.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What kind of pipeline segment this is.</summary>
public enum IrSegmentKind
{
    /// <summary>
    /// A function call target. <see cref="FunctionName"/> is set; arguments may
    /// contain <see cref="IrPipelinePlaceholder"/> entries marking where pipeline
    /// values insert.
    /// </summary>
    FunctionCall,

    /// <summary>
    /// A variable tap (assignment): the pipeline result is stored into a variable.
    /// <see cref="VariableName"/> is set; this is the terminal `> var` form.
    /// </summary>
    Variable,
}

/// <summary>
/// One ordered segment of a pipeline. Either a function call (with placeholders in
/// its arguments) or a variable tap. Pure data; the lowering layer interprets it.
/// </summary>
public sealed record IrSegment
{
    public required IrSegmentKind Kind { get; init; }

    /// <summary>Function name (short form). Set when Kind == FunctionCall.</summary>
    public string? FunctionName { get; init; }

    /// <summary>Full dotted method path for plugin/external calls. Null for builtins/helpers.</summary>
    public string? FullFunctionName { get; init; }

    /// <summary>Variable name. Set when Kind == Variable.</summary>
    public string? VariableName { get; init; }

    /// <summary>
    /// The segment's argument expressions, in source order. Entries may be
    /// <see cref="IrPipelineArgument.Placeholder"/> (a `_` slot to be filled from
    /// pipeline inputs) or <see cref="IrPipelineArgument.Literal"/> (a verbatim
    /// argument string).
    /// </summary>
    public ImmutableArray<IrPipelineArgument> Arguments { get; init; } = [];

    // ── Equality: ImmutableArray defaults to reference equality, so override to
    // compare Arguments by content. The other fields are scalar.

    public bool Equals(IrSegment? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Kind == other.Kind
            && FunctionName == other.FunctionName
            && FullFunctionName == other.FullFunctionName
            && VariableName == other.VariableName
            && Arguments.SequenceEqual(other.Arguments);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(FunctionName);
        hash.Add(FullFunctionName);
        hash.Add(VariableName);
        foreach (var a in Arguments) hash.Add(a);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A single argument slot of a pipeline segment. Discriminated union encoded as a
/// record with a Kind so it serialises cleanly to JSON.
/// </summary>
public sealed record IrPipelineArgument
{
    public required IrPipelineArgumentKind Kind { get; init; }

    /// <summary>
    /// Placeholder ordinal (0-based among multiple `_` in the same segment). Used to
    /// match pipeline values to positions when a segment has more than one `_`.
    /// Set when Kind == Placeholder.
    /// </summary>
    public int PlaceholderIndex { get; init; }

    /// <summary>
    /// Verbatim argument expression text (literal / PubVar / identifier). Set when
    /// Kind == Literal.
    /// </summary>
    public string? Literal { get; init; }

    public static IrPipelineArgument Placeholder(int index) =>
        new() { Kind = IrPipelineArgumentKind.Placeholder, PlaceholderIndex = index };

    public static IrPipelineArgument Lit(string literal) =>
        new() { Kind = IrPipelineArgumentKind.Literal, Literal = literal };
}

public enum IrPipelineArgumentKind
{
    Placeholder,
    Literal,
}
