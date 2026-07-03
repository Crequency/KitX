namespace KitX.WorkflowIR.Session;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowSession — one live editing session for one workflow document.
//
// Holds the single-truth IrWorkflow and fires IrChanged whenever either side
// (BS or BP) writes a change back through the SyncService. Lifecycle = one open
// workflow document. Replaces the legacy mutable Conversion.WorkflowSession
// (which held a mutable ControlFlowGraph); here the IR is immutable, so a "write"
// is a wholesale replacement of the IrWorkflow reference (the SyncService computes
// the new IR via IrDiffApply and assigns it).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A live editing session holding the single-truth immutable IrWorkflow. BS/BP
/// edits flow back through <see cref="SyncService"/>, which replaces
/// <see cref="Ir"/> with the new IR and fires <see cref="IrChanged"/>.
/// </summary>
public sealed class WorkflowSession
{
    /// <summary>The single-truth IR. Replaced (not mutated) on each edit.</summary>
    public IrWorkflow Ir { get; internal set; }

    /// <summary>Helper functions available to the script.</summary>
    public List<HelperFunction> HelperFunctions { get; set; } = [];

    /// <summary>
    /// Fired after a BS or BP edit was applied. Receives the IrChangeSet describing
    /// what changed, so listeners can do a focused re-render rather than rebuilding
    /// their whole view.
    /// </summary>
    public event Action<IrChangeSet>? IrChanged;

    public WorkflowSession(IrWorkflow ir)
    {
        Ir = ir ?? throw new ArgumentNullException(nameof(ir));
    }

    /// <summary>Replaces the IR and fires <see cref="IrChanged"/>.</summary>
    internal void ApplyChange(IrWorkflow newIr, IrChangeSet changeSet)
    {
        Ir = newIr;
        IrChanged?.Invoke(changeSet);
    }
}
