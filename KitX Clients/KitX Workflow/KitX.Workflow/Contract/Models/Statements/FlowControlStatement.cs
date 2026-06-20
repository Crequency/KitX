using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Contract.Models;

/// <summary>
/// Flow control statement
/// </summary>
public class FlowControlStatement : BlockStatement
{
    /// <summary>
    /// Type of flow control
    /// </summary>
    public FlowControlType ControlType { get; set; }

    /// <summary>
    /// Condition expression (for Branch/Loop)
    /// </summary>
    public string ConditionExpression { get; set; } = string.Empty;

    /// <summary>
    /// Shared control-flow arms model (Arms / TrueBlockName / FalseBlockName / LoopbackTarget).
    /// Embedded once here instead of duplicating the accessors and SetArm across
    /// FlowControlStatement and CFGStatement. The delegating properties below keep the public
    /// surface (<c>flow.Arms</c>, <c>flow.TrueBlockName</c>, ...) unchanged.
    /// </summary>
    private readonly ControlFlowArms _controlFlowArms = new();

    /// <summary>
    /// Outgoing arms of this control-flow statement. Replaces the former fixed
    /// <c>TrueBlockName</c>/<c>FalseBlockName</c>/<c>ToLoopCondReturnTo</c> triple.
    /// See <see cref="ControlFlowArms"/> for the per-arm layout of each control-flow kind.
    /// </summary>
    public List<BranchArm> Arms
    {
        get => _controlFlowArms.Arms;
        set => _controlFlowArms.Arms = value;
    }

    /// <summary>
    /// Convenience: the true-branch target (Arms[0].TargetBlockName for Branch/Loop).
    /// Kept for readability at call sites that conceptually deal with a two-way branch.
    /// Returns <c>string.Empty</c> when no arm is set (non-null, for SourceCode safety).
    /// </summary>
    public string TrueBlockName
    {
        get => _controlFlowArms.TrueBlockName ?? string.Empty;
        set => _controlFlowArms.TrueBlockName = value;
    }

    /// <summary>
    /// Convenience: the false-branch target (Arms[1].TargetBlockName for Branch/Loop).
    /// Returns <c>string.Empty</c> when no arm is set (non-null, for SourceCode safety).
    /// </summary>
    public string FalseBlockName
    {
        get => _controlFlowArms.FalseBlockName ?? string.Empty;
        set => _controlFlowArms.FalseBlockName = value;
    }

    /// <summary>
    /// The ToLoopCond loopback target — the loop condition block this statement returns to.
    /// Unified into <see cref="Arms"/>[0] (PinName="Exec", IsLoopback=true) so ToLoopCond is
    /// treated uniformly with Branch/Loop/Switch: all control-flow targets live in Arms.
    /// </summary>
    public string? LoopbackTarget
    {
        get => _controlFlowArms.LoopbackTarget;
        set => _controlFlowArms.LoopbackTarget = value;
    }

    /// <summary>
    /// Regenerates SourceCode from current field values.
    /// Call after updating Arms/ConditionExpression/etc. to keep SourceCode in sync.
    /// </summary>
    public void RegenerateSourceCode()
    {
        SourceCode = ControlType switch
        {
            FlowControlType.Branch => $"NextBlock = Branch({ConditionExpression}, \"{TrueBlockName}\", \"{FalseBlockName}\");",
            FlowControlType.Loop => $"NextBlock = Loop({ConditionExpression}, \"{TrueBlockName}\", \"{FalseBlockName}\");",
            FlowControlType.ToLoopCond => !string.IsNullOrEmpty(LoopbackTarget)
                ? $"NextBlock = ToLoopCond(\"{LoopbackTarget}\");"
                : "ToLoopCond();",
            FlowControlType.Switch => RegenerateSwitchSource(),
            FlowControlType.Break => "Break();",
            _ => SourceCode
        };
    }

    private string RegenerateSwitchSource()
    {
        // Arms layout: [Default, 0, 1, ..., N-1]
        if (Arms.Count == 0) return "Switch();";
        var defaultBlock = Arms[0].TargetBlockName;
        var blocks = Arms.Skip(1).Select(a => $"\"{a.TargetBlockName}\"");
        return $"NextBlock = Switch({ConditionExpression}, \"{defaultBlock}\", {string.Join(", ", blocks)});";
    }
}
