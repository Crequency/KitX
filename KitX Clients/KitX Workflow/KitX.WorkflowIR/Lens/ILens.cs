namespace KitX.WorkflowIR.Lens;

using KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// ILens — the projection/absorption contract for an IR view.
//
// The Lens pattern is the spine of the greenfield redesign: the immutable IrWorkflow
// is the single source of truth, and every external representation (BS text, BP
// graph, C# source) is a *view* projected from it. A lens is the bidirectional
// bridge between the IR and one view:
//
//   • Project(ir)         — IR → view: a pure read of the IR into the view's shape.
//   • Diff(baseline,delta)— view delta → IR diff: fold the view's user edit back into
//                           the IR as a content-addressed IrDiff (IrFingerprint-keyed).
//
// Why this cures the legacy mess: the old architecture had parallel mutable models
// (CFG, BP, generated C#) that each owned a private copy of the truth and were kept
// in step by hand-written, fallible converters (BS↔CFG, CFG↔BP, …). The lens makes
// the IR the sole truth and each view a derivation, so there is exactly one source
// and N pure projections — no bidirectional sync hazard.
//
// The delta type TDelta is view-specific: for BS text it is the edited text (or a
// structural delta); for BP it is the node edit stream. Each lens' Diff
// re-derives a fresh IR from the delta and computes a content-addressed IrDiff.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Bidirectional projection contract between the immutable <see cref="IrWorkflow"/>
/// and a view of type <typeparamref name="TView"/>. The delta type
/// <typeparamref name="TDelta"/> is the shape of an incremental view edit.
/// </summary>
/// <typeparam name="TView">The view representation (e.g. <c>string</c> for BS text).</typeparam>
/// <typeparam name="TDelta">The shape of an incremental edit to that view.</typeparam>
public interface ILens<TView, TDelta>
{
    /// <summary>
    /// Projects the IR into the view representation. A pure read — the IR is not
    /// mutated, and the same IR always yields the same view.
    /// </summary>
    TView Project(IrWorkflow ir);

    /// <summary>
    /// Folds a view delta back into an <see cref="IrDiff"/> against a baseline IR.
    /// The diff is content-addressed (keyed by <see cref="IrFingerprint"/>) so that
    /// unchanged statements are never needlessly rewritten.
    /// </summary>
    /// <remarks>
    /// Implementations re-derive a fresh IR from <paramref name="delta"/> (e.g. a BS
    /// lens re-parses the edited text) and delegate to <see cref="IrDiffer.Compute"/>
    /// for the alignment. The resulting <see cref="IrDiff"/> is applied to the
    /// baseline by <c>IrDiffApply.Apply</c> to yield the next IR (with Layout
    /// annotations reconciled so unchanged nodes keep their canvas positions).
    /// </remarks>
    IrDiff Diff(IrWorkflow baseline, TDelta delta);
}
