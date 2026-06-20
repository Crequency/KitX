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
    /// The ToLoopCond loopback target — the loop condition block this statement returns to.
    /// Unified into <see cref="Arms"/>[0] (PinName="Exec", IsLoopback=true) so ToLoopCond is
    /// treated uniformly with Branch/Loop/Switch: all control-flow targets live in Arms.
    /// The former standalone <c>ToLoopCondReturnTo</c> field was a patch over an Arms[0]
    /// collision that no longer exists; Loop statements no longer carry this metadata at all
    /// (it was dead — never read for control flow, only copied and debug-printed).
    /// </summary>
    public string? LoopbackTarget
    {
        get => Arms.Count > 0 ? Arms[0].TargetBlockName : null;
        set => SetArm(0, "Exec", value ?? string.Empty, isLoopback: true);
    }

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
