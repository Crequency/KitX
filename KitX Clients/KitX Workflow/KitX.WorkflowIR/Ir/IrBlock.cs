namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrBlock — an immutable basic block, with view state exiled to Annotations.
//
// The legacy CFGBlock was a mutable class carrying statements, block vars,
// successors, AND view state (LayoutX, LayoutY, NodePositions). Mixing view state
// into the semantic model broke structural equality and meant re-parsing BS
// (which has no notion of canvas position) silently dropped coordinates.
//
// IrBlock carries only semantic data. Block-level layout and per-node positions
// live in <see cref="Annotations"/> as IrAnnotation(Kind=Layout) entries:
//   • Block anchor: Key="BlockPos",   Value=IrLayout(X, Y)
//   • Per-node pos: Key=<stableId>,   Value=IrLayout(X, Y)
// This keeps them out of record equality while still surviving IR updates and
// .kcs round-trips.
//
// The block's <see cref="Kind"/> (Entry/Basic/BranchHeader) replaces the legacy
// Type enum (renamed to avoid the keyword clash with `type`).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Structural role of a block in the control flow.</summary>
public enum IrBlockKind
{
    /// <summary>The entry block (MainBlock). Exactly one per workflow.</summary>
    Entry,

    /// <summary>A plain sequential block; ends with a Sequential edge or nothing.</summary>
    Basic,

    /// <summary>A block ending with a control-flow terminator (Branch/ForLoop/Switch/Goto/Break/Exit).</summary>
    BranchHeader,
}

/// <summary>
/// An immutable basic block: a maximal sequence of statements with a single entry
/// and a single exit (or control-flow divergence at the end). Statements are flat
/// pipeline ASTs or control-flow terminators; there are no nested imperative
/// statements stored here (lowering expands pipelines on demand).
/// </summary>
/// <remarks>
/// <see cref="Annotations"/> is deliberately EXCLUDED from record equality: it is
/// view/render metadata (canvas positions, comments, source positions) that must
/// not affect whether two blocks are semantically the same. This is the fix for
/// the legacy CFGBlock, which mixed LayoutX/Y into the model and so made
/// re-rendered graphs unequal to their originals.
/// </remarks>
public sealed record IrBlock
{
    /// <summary>
    /// Block name — stable across round-trips. For the entry block this is
    /// "#MainBlock"; for named blocks the user-defined name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Structural role of this block.</summary>
    public required IrBlockKind Kind { get; init; }

    /// <summary>
    /// Ordered statements in this block. Each entry is either an
    /// <see cref="IrPipelineStatement"/> (functional `>` syntax, carried as AST)
    /// or an <see cref="IrControlFlowStatement"/> (a terminator).
    /// </summary>
    public ImmutableArray<IrStatement> Statements { get; init; } = [];

    /// <summary>Block-local variable declarations (##BlockVars).</summary>
    public ImmutableArray<IrBlockVar> BlockVars { get; init; } = [];

    /// <summary>
    /// Typed outgoing edges to successor blocks. A block can have multiple
    /// successors (e.g. BranchHeader → True/False). Sequential fall-through is a
    /// <see cref="IrEdgeType.Sequential"/> edge here — the single source of truth
    /// for "what runs next".
    /// </summary>
    public ImmutableArray<IrEdge> Successors { get; init; } = [];

    /// <summary>
    /// True when the block body was introduced by an explicit <c>##BlockBody</c>
    /// marker (required when BlockVars is non-empty, optional otherwise).
    /// </summary>
    public bool HasExplicitBlockBody { get; init; }

    /// <summary>
    /// View/render metadata for this block and its statements: block anchor
    /// position, per-node positions, comments, source positions. EXCLUDED from
    /// <see cref="Equals(IrBlock)"/>; survives identity-preserving IR updates and
    /// .kcs round-trips.
    /// </summary>
    public ImmutableArray<IrAnnotation> Annotations { get; init; } = [];

    /// <summary>True when this is the entry block.</summary>
    public bool IsEntry => Kind == IrBlockKind.Entry;

    /// <summary>
    /// The sequential fall-through target (the ToBlockName of the Sequential edge,
    /// if any). Derived from <see cref="Successors"/>; equivalent to the legacy
    /// NextBlockName/FallThroughTarget but always in sync.
    /// </summary>
    public string? FallThroughTarget =>
        Successors.FirstOrDefault(e => e.Type == IrEdgeType.Sequential)?.ToBlockName;

    // ── Equality: every field EXCEPT Annotations. ──
    // Annotations are view state (canvas positions, comments) and must not affect
    // semantic equality. We override the record-synthesised Equals/GetHashCode to
    // compare only the semantic fields, so two blocks that differ only in where
    // they are drawn on the canvas are equal.

    public bool Equals(IrBlock? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && Kind == other.Kind
            && HasExplicitBlockBody == other.HasExplicitBlockBody
            && Statements.SequenceEqual(other.Statements)
            && BlockVars.SequenceEqual(other.BlockVars)
            && Successors.SequenceEqual(other.Successors);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Kind);
        hash.Add(HasExplicitBlockBody);
        foreach (var s in Statements) hash.Add(s);
        foreach (var v in BlockVars) hash.Add(v);
        foreach (var e in Successors) hash.Add(e);
        return hash.ToHashCode();
    }
}
