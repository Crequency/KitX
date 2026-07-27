namespace KitX.WorkflowV6.Session;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Lens.BpGraphLens;

// ─────────────────────────────────────────────────────────────────────────────
// SyncService — the top-level coordinator that turns KS/BP edits into IR updates.
//
// Inherited concept from KitX.WorkflowIR.Session.SyncService, re-typed for the
// structured IR. Both KS edits and BP edits go through the same shape: produce a
// WorkflowDiff, apply it via the pure applier, replace the session's IR, fire
// IrChanged.
//
//   KS edit path:  new KS text → KsTextLens (re-parse + lower) → new IR →
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
/// Coordinates KS/BP edits into IR updates. One instance per workflow session;
/// constructed with the registries/lenses the session needs.
/// </summary>
public sealed class SyncService
{
    private readonly BuiltinFunctionRegistry _registry;
    private readonly KsTextLens _ksLens;

    public SyncService(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _ksLens = new KsTextLens(registry);
    }

    /// <summary>
    /// Applies a KS text edit: re-parses the new source, diffs against the live IR,
    /// applies the diff, and fires <see cref="WorkflowSession.IrChanged"/>.
    /// </summary>
    public WorkflowChangeSet ApplyKsEdit(WorkflowSession session, string newKsSource)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(newKsSource);

        // Parse the new KS source into a fresh IR.
        var newIr = _ksLens.Parse(newKsSource, session.HelperFunctions);

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
    /// <remarks>
    /// <b>Deferred to the project's P2 milestone</b> (dual-pane live highlight). The
    /// current v5.1-era <see cref="BpEditAction"/> hierarchy carries Block-centric
    /// fields (BlockName/AddBlock/RenameBlock/SetControlFlowArm) that have no V6
    /// equivalent — V6 has no "Block" concept (per KScriptGrammarRule §0/§16). A full
    /// V6-native redesign is required before this path can be wired correctly.
    ///
    /// <b>Why it is OK to defer</b>:
    /// <list type="bullet">
    /// <item><c>ApplyKsEdit</c> (KS→IR sync) is fully functional and independent of this path.</item>
    /// <item>BP→KS round-trip uses <c>BpReverseTranslator.Reverse</c> + <c>KsRenderer.Project</c> to produce a wholesale new KS text — KS is always formatted output, so no diff is needed for this direction.</item>
    /// <item>KS→BP minimal-change rendering is driven by <see cref="WorkflowChangeSet.AffectedPaths"/> emitted from <c>ApplyKsEdit</c>; the frontend re-renders only affected nodes. This is also independent of this path.</item>
    /// <item>The dual-pane live-highlight feature (the actual consumer of this method) is in the project's P2 priority — see <c>Package/Archive/Docs/V6-BpEditAction-Future-Design-ADR.md</c>.</item>
    /// </list>
    /// </remarks>
    public WorkflowChangeSet ApplyBpEdits(WorkflowSession session, IReadOnlyList<BpEditAction> edits) =>
        throw new NotSupportedException(
            "SyncService.ApplyBpEdits is deferred until the P2 'dual-pane live highlight' milestone. " +
            "KS→BP sync (ApplyKsEdit) is independent and fully functional. " +
            "See Package/Archive/Docs/V6-BpEditAction-Future-Design-ADR.md for the future design.");
}