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
    /// Function name in BS source (e.g. "Branch", "ForLoop", "Exit").
    /// Set during parsing; converters read this instead of inferring from ControlType.
    /// </summary>
    public string? FunctionName { get; set; }

    /// <summary>
    /// Condition expression (for Branch/Loop)
    /// </summary>
    public string ConditionExpression { get; set; } = string.Empty;

    /// <summary>
    /// Extra positional arguments carried by variadic control-flow forms.
    /// <para>
    /// ForLoop(from, to, step, indexName, bodyBlock, endBlock) stores <c>[from, to, step, indexName]</c>
    /// here (the body/end targets live in <see cref="Arms"/>). Other shapes leave this empty.
    /// Carried verbatim into <see cref="CFGStatement.Arguments"/> so <c>EmitStatements</c> can
    /// resolve them without re-parsing source text.
    /// </para>
    /// </summary>
    public List<string> FlowArguments { get; set; } = [];

    /// <summary>
    /// Shared control-flow arms model (Arms / TrueBlockName / FalseBlockName).
    /// v5.0: LoopbackTarget removed (ToLoopCond deleted). Goto uses Arms[0] (Exec arm).
    /// </summary>
    private readonly ControlFlowArms _controlFlowArms = new();

    /// <summary>
    /// Outgoing arms of this control-flow statement.
    /// See <see cref="ControlFlowArms"/> for the per-arm layout of each control-flow kind.
    /// </summary>
    public List<BranchArm> Arms
    {
        get => _controlFlowArms.Arms;
        set => _controlFlowArms.Arms = value;
    }

    /// <summary>
    /// Convenience: the first arm's target (Arms[0].TargetBlockName).
    /// Used by Branch (true), ForLoop (body), Goto (target), Switch (default).
    /// Returns <c>string.Empty</c> when no arm is set (non-null, for SourceCode safety).
    /// </summary>
    public string TrueBlockName
    {
        get => _controlFlowArms.TrueBlockName ?? string.Empty;
        set => _controlFlowArms.TrueBlockName = value;
    }

    /// <summary>
    /// Convenience: the second arm's target (Arms[1].TargetBlockName).
    /// Used by Branch (false), ForLoop (end).
    /// Returns <c>string.Empty</c> when no arm is set (non-null, for SourceCode safety).
    /// </summary>
    public string FalseBlockName
    {
        get => _controlFlowArms.FalseBlockName ?? string.Empty;
        set => _controlFlowArms.FalseBlockName = value;
    }

    /// <summary>
    /// Regenerates SourceCode from current field values.
    /// Call after updating Arms/ConditionExpression/etc. to keep SourceCode in sync.
    /// </summary>
    public void RegenerateSourceCode()
    {
        SourceCode = RenderSource(ControlType, ConditionExpression, Arms, FlowArguments);
    }

    /// <summary>
    /// Single source of truth for rendering a control-flow statement back to BlockScript text.
    /// v5.0: <c>loopbackTarget</c> parameter removed — Goto targets are Arms[0].TargetBlockName.
    /// <list type="bullet">
    /// <item><c>ConditionalJump</c>: Branch(cond, "true", "false").</item>
    /// <item><c>IterativeCounted</c>: ForLoop(from, to, step, indexName, "body", "end") —
    /// <paramref name="flowArguments"/> carries [from, to, step, indexName]; arms[0]/arms[1] are body/end.</item>
    /// <item><c>UnconditionalJump</c>: Goto("target") — arms[0].TargetBlockName.</item>
    /// <item><c>IndexedDispatch</c>: Switch(cond, "default", "b0", "b1", ...) — arms[0] is default.</item>
    /// <item><c>LoopExit</c>: Break().</item>
    /// </list>
    /// </summary>
    public static string RenderSource(FlowControlType shape, string condition,
        IReadOnlyList<BranchArm> arms,
        IReadOnlyList<string>? flowArguments = null) => shape switch
    {
        FlowControlType.ConditionalJump =>
            $"Branch({condition}, \"{arms.ElementAtOrDefault(0)?.TargetBlockName ?? ""}\", \"{arms.ElementAtOrDefault(1)?.TargetBlockName ?? ""}\");",
        FlowControlType.IterativeCounted =>
            RenderForLoopSource(flowArguments ?? [], arms),
        FlowControlType.UnconditionalJump => !string.IsNullOrEmpty(arms.ElementAtOrDefault(0)?.TargetBlockName)
            ? $"Goto(\"{arms[0].TargetBlockName}\");"
            : "Goto();",
        FlowControlType.IndexedDispatch => RenderSwitchSource(condition, arms),
        FlowControlType.ScriptReturn => "Exit();",
        _ => string.Empty
    };

    private static string RenderForLoopSource(IReadOnlyList<string> flowArguments, IReadOnlyList<BranchArm> arms)
    {
        // ForLoop(from, to, step, indexName, "body", "end")
        var from = flowArguments.ElementAtOrDefault(0) ?? "0";
        var to = flowArguments.ElementAtOrDefault(1) ?? "0";
        var step = flowArguments.ElementAtOrDefault(2) ?? "1";
        var indexName = flowArguments.ElementAtOrDefault(3) ?? "i";
        var body = arms.ElementAtOrDefault(0)?.TargetBlockName ?? "";
        var end = arms.ElementAtOrDefault(1)?.TargetBlockName ?? "";
        return $"ForLoop({from}, {to}, {step}, \"{indexName}\", \"{body}\", \"{end}\");";
    }

    private static string RenderSwitchSource(string condition, IReadOnlyList<BranchArm> arms)
    {
        // Arms layout: [Default, 0, 1, ..., N-1]
        if (arms.Count == 0) return $"Switch({condition}, \"\");";
        var defaultBlock = arms[0].TargetBlockName;
        var blocks = arms.Skip(1).Select(a => $"\"{a.TargetBlockName}\"");
        return $"Switch({condition}, \"{defaultBlock}\", {string.Join(", ", blocks)});";
    }
}
