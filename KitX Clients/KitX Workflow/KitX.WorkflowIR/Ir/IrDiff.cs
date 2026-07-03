namespace KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrDiff — Phase 6 placeholder.
//
// The real diff model (a sequence of add/remove/move/replace operations keyed by
// IrFingerprint) is designed and implemented in Phase 6. It is declared here as a
// minimal stub so that ILens<TView, TDelta>.Diff can be type-checked today; the
// Diff method bodies themselves are also deferred to Phase 6.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Placeholder for the Phase 6 IR diff model. Represents the delta between two
/// IrWorkflow snapshots. Phase 6 will replace this with a proper operation list.
/// </summary>
public sealed record IrDiff
{
    /// <summary>Placeholder operations list — populated by Phase 6.</summary>
    public ImmutableArray<IrDiffEntry> Entries { get; init; } = [];
}

/// <summary>Abstract base for a single diff operation (Phase 6).</summary>
public abstract record IrDiffEntry;
