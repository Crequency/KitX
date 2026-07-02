using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

public interface ICfgBsRenderer
{
    string Render(ControlFlowGraph cfg);
}
