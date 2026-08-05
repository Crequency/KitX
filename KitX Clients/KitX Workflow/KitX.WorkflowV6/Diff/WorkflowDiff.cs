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
/// <see cref="WorkflowDiffer"/>; consumed by <see cref="WorkflowDiffApply"/>.
/// </summary>
public sealed record WorkflowDiff
{
    /// <summary>
    /// Statement-level changes. Unchanged statements are deliberately absent — the
    /// diff only describes what moved.
    /// </summary>
    public ImmutableArray<StatementChange> StatementChanges { get; init; } = [];

    /// <summary>
    /// Declaration-section changes (Constants / GlobalVars / HelperFunctions),
    /// aligned by declaration name. Empty for body-only diffs.
    /// </summary>
    public ImmutableArray<DeclarationChange> DeclarationChanges { get; init; } = [];

    /// <summary>True when the diff describes no changes (statements nor declarations).</summary>
    public bool IsEmpty => StatementChanges.IsEmpty && DeclarationChanges.IsEmpty;
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

    /// <summary>
    /// Baseline index of the affected statement. For Removed it equals <see cref="Index"/>;
    /// for Modified it is the statement's index in the BASELINE body — which may differ
    /// from <see cref="Index"/> when surrounding statements were removed/added. Null for
    /// Added, or when a caller constructs the change manually (the applier then falls
    /// back to <see cref="Index"/>). Carried so the applier can rebuild the target list
    /// without re-running the diff (a pure reorder like [A,B] → [B,A] cannot be applied
    /// by any remove-then-insert order).
    /// </summary>
    public int? OldIndex { get; init; }
}

/// <summary>Which declaration section a <see cref="DeclarationChange"/> targets.</summary>
public enum DeclarationSection
{
    /// <summary>The <c>const { ... }</c> block (<see cref="Workflow.Constants"/>).</summary>
    Constants,

    /// <summary>The <c>var { ... }</c> block (<see cref="Workflow.GlobalVars"/>).</summary>
    GlobalVars,

    /// <summary>The helper functions (<see cref="Workflow.HelperFunctions"/>).</summary>
    HelperFunctions,
}

/// <summary>
/// One change inside a declaration section, aligned by <see cref="Name"/> (the stable
/// identity of a declaration). <see cref="NewValue"/> is the new declaration value for
/// Added / Modified and null for Removed. The value's concrete type depends on
/// <see cref="Section"/>: <see cref="Constant"/> / <see cref="GlobalVar"/> /
/// <see cref="KitX.Core.Contract.Workflow.HelperFunction"/>.
/// </summary>
public sealed record DeclarationChange
{
    /// <summary>Which declaration section this change belongs to.</summary>
    public required DeclarationSection Section { get; init; }

    /// <summary>The declaration's name (stable identity across edits).</summary>
    public required string Name { get; init; }

    /// <summary>Added / Removed / Modified.</summary>
    public required DiffKind Kind { get; init; }

    /// <summary>
    /// The new declaration value. Populated for Added / Modified; null for Removed.
    /// </summary>
    public object? NewValue { get; init; }
}
