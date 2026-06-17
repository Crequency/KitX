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
    /// Target block name when condition is true (for Branch/Loop)
    /// </summary>
    public string TrueBlockName { get; set; } = string.Empty;

    /// <summary>
    /// Target block name when condition is false (for Branch/Loop)
    /// For Loop: this is the loop exit block
    /// </summary>
    public string FalseBlockName { get; set; } = string.Empty;

    /// <summary>
    /// For ToLoopCond: the block name containing the Loop statement to return to
    /// </summary>
    public string? ToLoopCondReturnTo { get; set; }

    /// <summary>
    /// Regenerates SourceCode from current field values.
    /// Call after updating TrueBlockName/FalseBlockName/etc. to keep SourceCode in sync.
    /// </summary>
    public void RegenerateSourceCode()
    {
        SourceCode = ControlType switch
        {
            FlowControlType.Branch => $"NextBlock = Branch({ConditionExpression}, \"{TrueBlockName}\", \"{FalseBlockName}\");",
            FlowControlType.Loop => $"NextBlock = Loop({ConditionExpression}, \"{TrueBlockName}\", \"{FalseBlockName}\");",
            FlowControlType.ToLoopCond => ToLoopCondReturnTo != null
                ? $"NextBlock = ToLoopCond(\"{ToLoopCondReturnTo}\");"
                : "ToLoopCond();",
            FlowControlType.Break => "Break();",
            _ => SourceCode
        };
    }
}