using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Models.Statements;

/// <summary>
/// Flow control statement. v5.0: ControlType has been eliminated — FunctionName
/// identifies the function. RenderSource lives on each IBuiltinFunctionDefinition,
/// not as a static switch here.
/// </summary>
public class FlowControlStatement : BlockStatement
{
    /// <summary>
    /// Function name in BS source (e.g. "Branch", "ForLoop", "Exit").
    /// </summary>
    public string? FunctionName { get; set; }

    /// <summary>
    /// Condition expression (for Branch/Switch)
    /// </summary>
    public string ConditionExpression { get; set; } = string.Empty;

    /// <summary>
    /// Extra positional arguments carried by variadic control-flow forms.
    /// ForLoop(from, to, step, indexName, bodyBlock, endBlock) stores
    /// [from, to, step, indexName] here.
    /// </summary>
    public List<string> FlowArguments { get; set; } = [];

    /// <summary>
    /// Shared control-flow arms model (Arms / TrueBlockName / FalseBlockName).
    /// v5.0: LoopbackTarget removed. Goto uses Arms[0] (Exec arm).
    /// </summary>
    private readonly ControlFlowArms _controlFlowArms = new();

    public List<BranchArm> Arms
    {
        get => _controlFlowArms.Arms;
        set => _controlFlowArms.Arms = value;
    }

    /// <summary>First arm's target (Branch true, ForLoop body, Goto target, Switch default).</summary>
    public string TrueBlockName
    {
        get => _controlFlowArms.TrueBlockName ?? string.Empty;
        set => _controlFlowArms.TrueBlockName = value;
    }

    /// <summary>Second arm's target (Branch false, ForLoop end).</summary>
    public string FalseBlockName
    {
        get => _controlFlowArms.FalseBlockName ?? string.Empty;
        set => _controlFlowArms.FalseBlockName = value;
    }
}
