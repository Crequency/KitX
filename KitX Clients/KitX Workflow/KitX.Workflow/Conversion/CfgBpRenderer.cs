using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

public class CfgBpRenderer : ICfgBpRenderer
{
    private readonly Blueprint.CFGGraphRenderer _renderer = new();
    public KitX.Core.Contract.Workflow.Blueprint Render(ControlFlowGraph cfg) => _renderer.Render(cfg);
}
