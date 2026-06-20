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
    /// Outgoing arms of this control-flow statement. Replaces the former fixed
    /// <c>TrueBlockName</c>/<c>FalseBlockName</c>/<c>ToLoopCondReturnTo</c> triple.
    /// <list type="bullet">
    /// <item>Branch: [<c>"True"</c>, <c>"False"</c>]</item>
    /// <item>Loop: [<c>"LoopBody"</c>, <c>"LoopEnd"</c>]</item>
    /// <item>ToLoopCond: [<c>"Exec"</c> with <see cref="BranchArm.IsLoopback"/>=true]</item>
    /// <item>Switch: [<c>"Default"</c>, <c>"0"</c>, <c>"1"</c>, ... , <c>"N-1"</c>]</item>
    /// </list>
    /// </summary>
    public List<BranchArm> Arms { get; set; } = [];

    /// <summary>
    /// Convenience: the true-branch target (Arms[0].TargetBlockName for Branch/Loop).
    /// Kept for readability at call sites that conceptually deal with a two-way branch.
    /// </summary>
    public string TrueBlockName
    {
        get => Arms.Count > 0 ? Arms[0].TargetBlockName : string.Empty;
        set => SetArm(0, "True", value);
    }

    /// <summary>
    /// Convenience: the false-branch target (Arms[1].TargetBlockName for Branch/Loop).
    /// </summary>
    public string FalseBlockName
    {
        get => Arms.Count > 1 ? Arms[1].TargetBlockName : string.Empty;
        set => SetArm(1, "False", value);
    }

    /// <summary>
    /// The ToLoopCond loopback target block name. Kept as a SEPARATE field (not routed through
    /// Arms[0]) so that setting it on a Loop/Branch statement — which <c>BlockStatementExtractor
    /// .CreateLoopBlocksForBlock</c> does to record the loop's own block as the loopback target —
    /// does NOT clobber <see cref="TrueBlockName"/> (Arms[0]). On a ToLoopCond statement this is
    /// the sole target; on a Loop/Branch statement it carries the owning loop's condition block
    /// for back-edge resolution and is independent of the Branch/Loop arms.
    /// </summary>
    public string? ToLoopCondReturnTo { get; set; }

    private void SetArm(int index, string pinName, string value, bool isLoopback = false)
    {
        while (Arms.Count <= index)
            Arms.Add(new BranchArm());
        Arms[index].PinName = pinName;
        Arms[index].TargetBlockName = value;
        Arms[index].IsLoopback = isLoopback;
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
            FlowControlType.ToLoopCond => !string.IsNullOrEmpty(ToLoopCondReturnTo)
                ? $"NextBlock = ToLoopCond(\"{ToLoopCondReturnTo}\");"
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
