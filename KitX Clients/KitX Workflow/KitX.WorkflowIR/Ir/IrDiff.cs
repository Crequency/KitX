namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrDiff — the immutable, content-addressed delta between two IrWorkflows.
//
// This is the greenfield successor to the legacy mutable CfgDiff (a POCO with
// five List<T> fields). Three things changed:
//
//   1. Immutability — every collection is an ImmutableArray. A diff is a value:
//      once computed it never changes, and two computes over the same inputs are
//      structurally equal.
//
//   2. Content-addressed identity — every change is keyed by IrFingerprint (the
//      content-derived, re-parse-stable identity of a statement), not by a
//      random Guid StatementId. So a diff survives a BS re-parse unchanged, which
//      the legacy diff (keyed on StatementId) could not.
//
//   3. One flat statement-change list — the legacy CfgDiff split Added/Removed/
//      Modified/Moved into four lists, forcing consumers to consult all four and
//      making it easy to miss a category. Here every statement change is one
//      StatementChange carrying a DiffKind; consumers filter by Kind.
//
// Block-level changes (rename = delete+insert, by design — block identity is name)
// live in BlockChanges; statement-level changes live in StatementChanges. The
// PositionsChanged flag signals that some statement moved index (so a BP renderer
// must re-layout affected nodes even though no content changed).
//
// The diff carries NO view state: layout coordinates are reconciled by IrDiffApply
// (which copies Layout annotations from the old IR for unchanged statements), not
// encoded as diff operations. This keeps the diff purely semantic.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The immutable delta between two <see cref="IrWorkflow"/> snapshots. Produced by
/// <c>IrDiffer.Compute</c>; consumed by <c>IrDiffApply.Apply</c>. Content-addressed:
/// every statement change is keyed by <see cref="IrFingerprint"/> so it survives a
/// BS re-parse.
/// </summary>
public sealed record IrDiff
{
    /// <summary>
    /// Block-level changes: blocks added or removed (block identity = name, so a
    /// rename is a remove of the old name plus an add of the new). Ordered: removals
    /// first, then additions.
    /// </summary>
    public ImmutableArray<BlockChange> BlockChanges { get; init; } = [];

    /// <summary>
    /// Statement-level changes inside common blocks: Added / Removed / Modified /
    /// Moved, each keyed by <see cref="StatementChange.Fingerprint"/>. Unchanged
    /// statements are deliberately absent — the diff only describes what moved.
    /// </summary>
    public ImmutableArray<StatementChange> StatementChanges { get; init; } = [];

    /// <summary>
    /// True when at least one statement changed its in-block index (a Move), which
    /// means a BP renderer must reflow node positions even though no statement
    /// content changed. Cleared for pure add/remove/modify.
    /// </summary>
    public bool PositionsChanged =>
        StatementChanges.Any(c => c.Kind == DiffKind.Moved);

    /// <summary>
    /// True when the diff describes no changes — the two IRs are semantically
    /// identical at the block + statement level. (Layout annotations are not
    /// considered; they are reconciled separately by IrDiffApply.)
    /// </summary>
    public bool IsEmpty => BlockChanges.IsEmpty && StatementChanges.IsEmpty;
}

/// <summary>Kind of a block-level change.</summary>
public enum BlockChangeKind
{
    /// <summary>The block exists in the new IR but not the old (an insertion).</summary>
    Added,

    /// <summary>The block existed in the old IR but not the new (a deletion).</summary>
    Removed,
}

/// <summary>
/// A block-level add or remove. For <see cref="BlockChangeKind.Added"/>,
/// <see cref="NewBlock"/> carries the full block from the new IR (so the applier
/// can splice it in without re-deriving it). For Removed it is null.
/// </summary>
public sealed record BlockChange
{
    /// <summary>Name of the block (the stable block identity).</summary>
    public required string Name { get; init; }

    /// <summary>Added or Removed.</summary>
    public required BlockChangeKind Kind { get; init; }

    /// <summary>
    /// The new block, populated for <see cref="BlockChangeKind.Added"/>. Null for
    /// Removed (the applier drops the block by name).
    /// </summary>
    public IrBlock? NewBlock { get; init; }
}

/// <summary>Kind of a statement-level change inside a common block.</summary>
public enum DiffKind
{
    /// <summary>The statement exists in the new IR but not the old (an insertion).</summary>
    Added,

    /// <summary>The statement existed in the old IR but not the new (a deletion).</summary>
    Removed,

    /// <summary>
    /// The statement's identity changed (its fingerprint differs) but it sits at the
    /// same slot — a content edit. <see cref="NewValue"/> carries the new statement.
    /// </summary>
    Modified,

    /// <summary>
    /// The statement's identity is unchanged but its in-block index moved (a reorder).
    /// <see cref="FromIndex"/>/<see cref="ToIndex"/> describe the relocation.
    /// </summary>
    Moved,
}

/// <summary>
/// A single statement-level change inside a common block. The <see cref="Fingerprint"/>
/// is the identity the change is reported under: for Added/Modified/Moved it is the
/// NEW statement's fingerprint (so the applier can locate the new value); for Removed
/// it is the OLD statement's fingerprint (so the applier can locate what to drop).
/// </summary>
public sealed record StatementChange
{
    /// <summary>Name of the block the change occurs in.</summary>
    public required string BlockName { get; init; }

    /// <summary>
    /// The fingerprint this change is keyed on (see remarks on the class). Content-
    /// derived and re-parse-stable, so the same edit always yields the same change.
    /// </summary>
    public required IrFingerprint Fingerprint { get; init; }

    /// <summary>Added / Removed / Modified / Moved.</summary>
    public required DiffKind Kind { get; init; }

    /// <summary>
    /// The new statement value. Populated for Added / Modified; null for Removed /
    /// Moved (Moved relocates the existing statement, which the applier already has).
    /// </summary>
    public IrStatement? NewValue { get; init; }

    /// <summary>
    /// Target index in the block's statement list. For Added/Moved this is where the
    /// statement lands; null for Removed/Modified (Modified replaces in place).
    /// </summary>
    public int? NewIndex { get; init; }

    /// <summary>Source index (old position) for a Moved statement; null otherwise.</summary>
    public int? FromIndex { get; init; }

    /// <summary>
    /// Destination block for a cross-block move. When set (and different from
    /// <see cref="BlockName"/>), the statement moved from <see cref="BlockName"/> into
    /// this block. The applier removes it from the source and inserts it at
    /// <see cref="NewIndex"/> in the destination.
    /// </summary>
    public string? ToBlock { get; init; }
}
