namespace KitX.WorkflowV6.Diff;

using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowDiffer — the semantic diff engine for the structured IR.
//
// Inherited concept from KitX.WorkflowIR.Diff.IrDiffer, re-targeted at the structured
// AST. The v5 algorithm aligned two block lists by Name and ran LCS over the
// fingerprint sequence of each common block; v6 aligns two trees by walking them in
// lexical order (depth-first pre-order over the structured statements) and running
// the same LCS-over-fingerprint logic per enclosing scope.
//
// Identity is the Fingerprint (content-derived, re-parse-stable), so a diff survives
// a BS re-parse unchanged. Layout annotations are NOT compared here — they are
// reconciled by the applier (TBD) which copies Layout from the baseline for
// unchanged statements.
//
// The actual algorithm ships with the implementation plan. The skeleton returns
// empty diffs so the SyncService round-trip compiles end-to-end today.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Computes a content-addressed <see cref="WorkflowDiff"/> between two immutable
/// <see cref="Workflow"/> snapshots. Pure: never mutates either input.
/// </summary>
public static class WorkflowDiffer
{
    /// <summary>
    /// Placeholder diff entry. Returns an empty diff until the structured-tree
    /// alignment algorithm ships with the implementation plan.
    /// </summary>
    public static WorkflowDiff Compute(Workflow oldIr, Workflow newIr)
    {
        ArgumentNullException.ThrowIfNull(oldIr);
        ArgumentNullException.ThrowIfNull(newIr);
        return new WorkflowDiff();
    }
}
