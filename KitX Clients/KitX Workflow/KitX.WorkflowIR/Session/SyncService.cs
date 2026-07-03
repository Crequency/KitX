namespace KitX.Workflow.Session;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Diff;
using KitX.Workflow.Ir;
using KitX.Workflow.Lens.BsTextLens;
using KitX.Workflow.Lens.BpGraphLens;

// ─────────────────────────────────────────────────────────────────────────────
// SyncService — the top-level coordinator that turns BS/BP edits into IR updates.
//
// This is the greenfield replacement for the legacy Conversion.BsSyncService +
// Blueprint.BpEditApplier pair. Both BS edits and BP edits go through the same
// shape: produce an IrDiff, apply it via the pure IrDiffApply, replace the
// session's IR, fire IrChanged.
//
//   BS edit path:  new BS text → BsTextLens (re-parse + lower) → new IR →
//                  IrDiffer.Compute(old, new) → IrDiff
//   BP edit path:  BpEditAction[] → BpEditTranslator → IrDiff
//
// Both then converge: IrDiffApply.Apply(session.Ir, diff) → new IR →
// session.ApplyChange(new IR, changeSet) → IrChanged fires → renderers re-render.
//
// Layout preservation: because IrDiffApply copies Layout annotations from the
// old IR for unchanged statements, a BS edit that adds one Print keeps the BP
// canvas positions of every other node — the central UX requirement (§7).
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
    /// Returns the change set describing what changed (empty if no change).
    /// </summary>
    public IrChangeSet ApplyBsEdit(WorkflowSession session, string newBsSource)
    {
        // BS → IR: re-parse + lower the new text into a fresh IR.
        var newIr = _bsLens.Parse(newBsSource, session.HelperFunctions);

        // Diff against the live IR.
        var diff = IrDiffer.Compute(session.Ir, newIr);
        if (diff.IsEmpty)
            return new IrChangeSet { StatementDiff = diff, AffectedBlocks = [] };

        // Apply: pure function returns the merged IR (Layout annotations for
        // unchanged statements are inherited from the old IR by IrDiffApply).
        var mergedIr = IrDiffApply.Apply(session.Ir, diff);

        var changeSet = new IrChangeSet
        {
            StatementDiff = diff,
            AffectedBlocks = IrChangeSet.CollectAffectedBlocks(diff),
        };
        session.ApplyChange(mergedIr, changeSet);
        return changeSet;
    }

    /// <summary>
    /// Applies a batch of BP edit actions: translates them to an IrDiff, applies
    /// it, and fires <see cref="WorkflowSession.IrChanged"/>. BP-name → IR-name
    /// resolution goes through the registry's IBpReverseHandler table (no
    /// hardcoded dictionary).
    /// </summary>
    public IrChangeSet ApplyBpEdits(WorkflowSession session, IReadOnlyList<BpEditAction> actions)
    {
        var translator = new BpEditTranslator(_registry);
        var diff = translator.Translate(session.Ir, actions);
        if (diff.IsEmpty)
            return new IrChangeSet { StatementDiff = diff, AffectedBlocks = [] };

        var mergedIr = IrDiffApply.Apply(session.Ir, diff);
        var changeSet = new IrChangeSet
        {
            StatementDiff = diff,
            AffectedBlocks = IrChangeSet.CollectAffectedBlocks(diff),
            PositionsChanged = diff.PositionsChanged,
        };
        session.ApplyChange(mergedIr, changeSet);
        return changeSet;
    }
}
