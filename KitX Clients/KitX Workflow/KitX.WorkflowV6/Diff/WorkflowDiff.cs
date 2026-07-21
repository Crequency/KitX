namespace KitX.WorkflowV6.Diff;

using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowDiff — the immutable, content-addressed delta between two structured
// workflows.
//
// Inherited concept from KitX.WorkflowIR's IrDiff, re-typed for the structured IR:
// where v5 keyed block-level changes by Name (block identity = name), v6 keys
// statement-level changes by lexical path (the path of nested scopes containing the
// statement). There are no block-level add/remove operations — the IR is a tree, not
// a list of blocks.
//
// Three statement-level change kinds are sufficient:
//   • Added      — a statement present in the new IR but not the baseline.
//   • Removed    — a statement present in the baseline but not the new IR.
//   • Modified   — a statement's identity (Fingerprint) changed at the same slot.
// (Move is an in-scope reorder; the structured IR represents it as Remove + Add at
//  adjacent positions, so no separate kind is needed. Cross-scope moves similarly
//  decompose to a Remove at the source path and an Add at the destination path.)
//
// Layout coordinates are NOT encoded as diff operations — they live in Annotations
// (excluded from equality). The applier reconciles layout separately by copying
// Layout annotations from the baseline for unchanged statements.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The immutable delta between two <see cref="Workflow"/> snapshots. Produced by
/// <see cref="WorkflowDiffer"/>; consumed by the applier (TBD during implementation).
/// </summary>
public sealed record WorkflowDiff
{
    /// <summary>
    /// Statement-level changes. Unchanged statements are deliberately absent — the
    /// diff only describes what moved.
    /// </summary>
    public ImmutableArray<StatementChange> StatementChanges { get; init; } = [];

    /// <summary>True when the diff describes no changes.</summary>
    public bool IsEmpty => StatementChanges.IsEmpty;
}

/// <summary>Kind of a statement-level change.</summary>
public enum DiffKind
{
    /// <summary>The statement exists in the new IR but not the baseline (an insertion).</summary>
    Added,

    /// <summary>The statement existed in the baseline but not the new IR (a deletion).</summary>
    Removed,

    /// <summary>
    /// The statement's identity changed (its Fingerprint differs) but it sits at the
    /// same lexical slot — a content edit. <see cref="StatementChange.NewValue"/> is populated.
    /// </summary>
    Modified,
}

/// <summary>
/// One statement-level change inside the structured IR. <see cref="LexicalPath"/> is
/// the "/"-separated scope path of the change (e.g. <c>"/"</c> for top-level, or
/// <c>"/0/body/3"</c> for the 4th statement inside the body of the 1st top-level
/// statement). <see cref="Fingerprint"/> is the content-derived identity the change
/// is reported under.
/// </summary>
public sealed record StatementChange
{
    /// <summary>Lexical path of the change (replaces v5's BlockName).</summary>
    public required string LexicalPath { get; init; }

    /// <summary>
    /// The fingerprint this change is keyed on. For Added/Modified it is the NEW
    /// statement's fingerprint; for Removed it is the OLD statement's fingerprint.
    /// </summary>
    public required Fingerprint Fingerprint { get; init; }

    /// <summary>Added / Removed / Modified.</summary>
    public required DiffKind Kind { get; init; }

    /// <summary>
    /// The new statement value. Populated for Added / Modified; null for Removed.
    /// </summary>
    public Statement? NewValue { get; init; }

    /// <summary>
    /// Target index in the enclosing scope's statement list. For Added this is where
    /// the statement lands; for Removed/Modified it is the position of the affected
    /// statement (Modified replaces in place).
    /// </summary>
    public int? Index { get; init; }
}
