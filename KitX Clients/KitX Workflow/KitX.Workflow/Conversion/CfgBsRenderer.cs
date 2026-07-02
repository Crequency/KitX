using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

public class CfgBsRenderer : ICfgBsRenderer
{
    public string Render(ControlFlowGraph cfg) => new CFGRenderer().Render(cfg);
}
