namespace KitX.Workflow.Contract.Models;

/// <summary>
/// Flow control statement types
/// </summary>
public enum FlowControlType
{
    /// <summary>
    /// Branch to another block based on condition
    /// </summary>
    Branch,

    /// <summary>
    /// Loop while condition is true (Loop has three args: condition, trueBlock, falseBlock)
    /// </summary>
    Loop,

    /// <summary>
    /// Return from script execution
    /// </summary>
    Return,

    /// <summary>
    /// Break from current loop
    /// </summary>
    Break,

    /// <summary>
    /// To loop condition - marks the end of a loop body and returns to loop condition
    /// </summary>
    ToLoopCond
}