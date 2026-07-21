namespace KitX.WorkflowV6.Session;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BsTextLens;
using KitX.WorkflowV6.Lens.BpGraphLens;

// ─────────────────────────────────────────────────────────────────────────────
// SyncService — the top-level coordinator that turns BS/BP edits into IR updates.
//
// Inherited concept from KitX.WorkflowIR.Session.SyncService, re-typed for the
// structured IR. Both BS edits and BP edits go through the same shape: produce a
// WorkflowDiff, apply it via the pure applier, replace the session's IR, fire
// IrChanged.
//
//   BS edit path:  new BS text → BsTextLens (re-parse + lower) → new IR →
//                  WorkflowDiffer.Compute(old, new) → WorkflowDiff
//   BP edit path:  BpEditAction[] → BpGraphLens → WorkflowDiff
//
// Both then converge: applier.Apply(session.Ir, diff) → new IR →
// session.ApplyChange(new IR, changeSet) → IrChanged fires → renderers re-render.
//
// Layout preservation: unchanged statements keep their canvas positions because
// layout lives in Annotations (excluded from equality) and the applier copies
// Layout annotations from the old IR for unchanged statements. This is the central
// UX requirement (discussion notes §7).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Coordinates BS/BP edits into IR updates. One instance per workflow session;
/// constructed with the registries/lenses the session needs.
/// </summary>
public sealed class SyncService
{
    private readonly BuiltinFunctionRegistry _registry;
    private readonly BsTextLens _bsLens;

    public SyncService(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _bsLens = new BsTextLens(registry);
    }

    /// <summary>
    /// Applies a BS text edit: re-parses the new source, diffs against the live IR,
    /// applies the diff, and fires <see cref="WorkflowSession.IrChanged"/>.
    /// </summary>
    public WorkflowChangeSet ApplyBsEdit(WorkflowSession session, string newBsSource)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(newBsSource);

        // Parse the new BS source into a fresh IR.
        var newIr = _bsLens.Parse(newBsSource, session.HelperFunctions);

        // If the new IR equals the current IR, nothing changed — don't fire event.
        if (session.Ir.Equals(newIr))
            return new WorkflowChangeSet { StatementDiff = null, AffectedPaths = [] };

        // Compute the content-addressed diff.
        var diff = WorkflowDiffer.Compute(session.Ir, newIr);
        if (diff.IsEmpty)
            return new WorkflowChangeSet { StatementDiff = null, AffectedPaths = [] };

        // Apply the diff to produce the new IR (with Layout preserved for unchanged
        // statements).
        var appliedIr = WorkflowDiffApply.Apply(session.Ir, diff);

        // Build the change set and update the session.
        var changeSet = new WorkflowChangeSet
        {
            StatementDiff = diff,
            AffectedPaths = WorkflowChangeSet.CollectAffectedPaths(diff),
        };
        session.ApplyChange(appliedIr, changeSet);
        return changeSet;
    }

    /// <summary>
    /// Applies a batch of BP edits: translates them into a WorkflowDiff, applies the
    /// diff, and fires <see cref="WorkflowSession.IrChanged"/>.
    /// </summary>
    public WorkflowChangeSet ApplyBpEdits(WorkflowSession session, IReadOnlyList<BpEditAction> edits) =>
        throw new NotImplementedException("SyncService.ApplyBpEdits: v6 BP lens not implemented (Phase 9).");
}