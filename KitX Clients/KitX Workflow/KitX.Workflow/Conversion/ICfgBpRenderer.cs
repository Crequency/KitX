using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

public interface ICfgBpRenderer
{
    KitX.Core.Contract.Workflow.Blueprint Render(ControlFlowGraph cfg);
}
