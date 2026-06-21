using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.Models.Statements;

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
    /// Delegates to the static <see cref="RenderSource"/> so every site that rebuilds BS
    /// control-flow source text (this instance method, BP2CFGConverter, CFG2BSConverter)
    /// shares one renderer and cannot drift out of sync.
    /// </summary>
    public void RegenerateSourceCode()
    {
        SourceCode = RenderSource(ControlType, ConditionExpression, Arms, LoopbackTarget);
    }

    /// <summary>
    /// Single source of truth for rendering a control-flow statement back to BlockScript text.
    /// Takes the authoritative shape plus the fields that vary per shape:
    /// <list type="bullet">
    /// <item><c>ConditionalJump</c>/<c>IterativeJump</c>: Branch/Loop(cond, "true", "false").</item>
    /// <item><c>LoopBackedge</c>: ToLoopCond("target") (or bare ToLoopCond() when no target).</item>
    /// <item><c>IndexedDispatch</c>: Switch(cond, "default", "b0", "b1", ...) — arms[0] is default.</item>
    /// <item><c>LoopExit</c>: Break().</item>
    /// </list>
    /// </summary>
    /// <param name="shape">The control-flow shape (FlowControlType).</param>
    /// <param name="condition">The condition/selector expression text (may be empty for ToLoopCond/Break).</param>
    /// <param name="arms">The arms; for Switch the layout is [Default, 0, 1, …]; for Branch/Loop
    /// arms[0]/arms[1] are true/false. Ignored for ToLoopCond/Break.</param>
    /// <param name="loopbackTarget">The ToLoopCond return-to block name (null/empty → bare ToLoopCond()).</param>
    public static string RenderSource(FlowControlType shape, string condition,
        IReadOnlyList<BranchArm> arms, string? loopbackTarget) => shape switch
    {
        FlowControlType.ConditionalJump =>
            $"NextBlock = Branch({condition}, \"{arms.ElementAtOrDefault(0)?.TargetBlockName ?? ""}\", \"{arms.ElementAtOrDefault(1)?.TargetBlockName ?? ""}\");",
        FlowControlType.IterativeJump =>
            $"NextBlock = Loop({condition}, \"{arms.ElementAtOrDefault(0)?.TargetBlockName ?? ""}\", \"{arms.ElementAtOrDefault(1)?.TargetBlockName ?? ""}\");",
        FlowControlType.LoopBackedge => !string.IsNullOrEmpty(loopbackTarget)
            ? $"NextBlock = ToLoopCond(\"{loopbackTarget}\");"
            : "ToLoopCond();",
        FlowControlType.IndexedDispatch => RenderSwitchSource(condition, arms),
        FlowControlType.LoopExit => "Break();",
        _ => string.Empty
    };

    private static string RenderSwitchSource(string condition, IReadOnlyList<BranchArm> arms)
    {
        // Arms layout: [Default, 0, 1, ..., N-1]
        if (arms.Count == 0) return $"NextBlock = Switch({condition}, \"\");";
        var defaultBlock = arms[0].TargetBlockName;
        var blocks = arms.Skip(1).Select(a => $"\"{a.TargetBlockName}\"");
        return $"NextBlock = Switch({condition}, \"{defaultBlock}\", {string.Join(", ", blocks)});";
    }
}
