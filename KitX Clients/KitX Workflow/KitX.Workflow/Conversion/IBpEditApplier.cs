using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Conversion;

/// <summary>
/// BP edit action → CFG direct mutation (Decision 2). Each <see cref="BpEditAction"/>
/// is translated into mutations on the live CFG.
/// </summary>
public interface IBpEditApplier
{
    CfgChangeSet ApplyBpEdit(IWorkflowSession session, BpEditAction action);

    CfgChangeSet ApplyBpEdits(IWorkflowSession session, IReadOnlyList<BpEditAction> actions);
}
