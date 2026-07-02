using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// A live editing session for one workflow document. Holds the single-truth CFG and fires
/// <see cref="CfgChanged"/> whenever either side (BS or BP) writes a change back.
/// Lifecycle = one open workflow document.
/// </summary>
public interface IWorkflowSession
{
    ControlFlowGraph Cfg { get; }
    event Action<CfgChangeSet>? CfgChanged;
    List<HelperFunction> HelperFunctions { get; set; }
}
