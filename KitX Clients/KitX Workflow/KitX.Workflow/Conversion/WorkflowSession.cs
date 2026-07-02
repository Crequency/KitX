using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

public class WorkflowSession : IWorkflowSession
{
    public ControlFlowGraph Cfg { get; }
    public event Action<CfgChangeSet>? CfgChanged;
    public List<HelperFunction> HelperFunctions { get; set; } = [];

    public WorkflowSession(ControlFlowGraph cfg)
    {
        Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
    }

    public void FireCfgChanged(CfgChangeSet changeSet)
    {
        CfgChanged?.Invoke(changeSet);
    }
}
