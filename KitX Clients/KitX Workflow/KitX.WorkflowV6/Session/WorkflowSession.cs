namespace KitX.WorkflowV6.Session;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowSession — one live editing session for one workflow document.
//
// Inherited contract from KitX.WorkflowIR.Session.WorkflowSession: holds the
// single-truth IR and fires IrChanged whenever either side (KS or BP) writes a
// change back through the SyncService. Lifecycle = one open workflow document.
// Because the IR is immutable, a "write" is a wholesale replacement of the IR
// reference (the SyncService computes the new IR via the applier and assigns it).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A live editing session holding the single-truth immutable <see cref="Workflow"/>.
/// KS/BP edits flow back through <see cref="SyncService"/>, which replaces
/// <see cref="Ir"/> with the new IR and fires <see cref="IrChanged"/>.
/// </summary>
public sealed class WorkflowSession
{
    /// <summary>The single-truth IR. Replaced (not mutated) on each edit.</summary>
    public Workflow Ir { get; internal set; }

    /// <summary>Helper functions available to the workflow.</summary>
    public List<HelperFunction> HelperFunctions { get; set; } = [];

    /// <summary>
    /// Fired after a KS or BP edit was applied. Receives the WorkflowChangeSet
    /// describing what changed, so listeners can do a focused re-render rather than
    /// rebuilding their whole view.
    /// </summary>
    public event Action<WorkflowChangeSet>? IrChanged;

    public WorkflowSession(Workflow ir)
    {
        Ir = ir ?? throw new ArgumentNullException(nameof(ir));
    }

    /// <summary>Replaces the IR and fires <see cref="IrChanged"/>.</summary>
    internal void ApplyChange(Workflow newIr, WorkflowChangeSet changeSet)
    {
        Ir = newIr;
        IrChanged?.Invoke(changeSet);
    }
}
