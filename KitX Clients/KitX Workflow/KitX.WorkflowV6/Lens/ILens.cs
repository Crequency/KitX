namespace KitX.WorkflowV6.Lens;

using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Diff;

// ─────────────────────────────────────────────────────────────────────────────
// ILens — the projection/absorption contract for an IR view.
//
// Inherited concept from KitX.WorkflowIR.Lens.ILens, unchanged in shape: the
// immutable <see cref="Workflow"/> IR is the single source of truth, and every
// external representation (KS text, BP graph, C# source) is a *view* projected from
// it. A lens is the bidirectional bridge between the IR and one view:
//
///   • Project(ir)         — IR → view: a pure read of the IR into the view's shape.
///   • Diff(baseline,delta)— view delta → IR diff: fold the view's user edit back into
///                           the IR as a content-addressed diff.
//
// Why a Lens (vs sync converters): the IR is the sole truth, each view is a
// derivation, so there is exactly one source and N pure projections — no bidirectional
// sync hazard. The v6 library ships two lenses: KsTextLens (indented-grammar) and
// BpGraphLens (structured-graph), with placeholder bodies pending the implementation
// plan.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Bidirectional projection contract between the immutable <see cref="Workflow"/>
/// and a view of type <typeparamref name="TView"/>. The delta type
/// <typeparamref name="TDelta"/> is the shape of an incremental view edit.
/// </summary>
public interface ILens<TView, TDelta>
{
    /// <summary>
    /// Projects the IR into the view representation. A pure read — the IR is not
    /// mutated, and the same IR always yields the same view.
    /// </summary>
    TView Project(Workflow ir);

    /// <summary>
    /// Folds a view delta back into a <see cref="WorkflowDiff"/> against a baseline IR.
    /// Content-addressed (keyed by <see cref="Fingerprint"/>) so unchanged statements
    /// are never needlessly rewritten.
    /// </summary>
    WorkflowDiff Diff(Workflow baseline, TDelta delta);
}
