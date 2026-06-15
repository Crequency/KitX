namespace KitX.Workflow.CFG;

/// <summary>
/// Type of a CFG block, indicating its structural role in the control flow.
/// </summary>
public enum CFGBlockType
{
    /// <summary>MainBlock — the entry point of the script.</summary>
    Entry,

    /// <summary>A plain sequential block with no control flow divergence at the end.</summary>
    Basic,

    /// <summary>A block ending with a Branch statement (condition → true/false targets).</summary>
    BranchHeader,

    /// <summary>A block ending with a Loop statement (condition → body/exit targets).</summary>
    LoopHeader,

    /// <summary>The body of a loop — may reach back to the LoopHeader via ToLoopCond.</summary>
    LoopBody,

    /// <summary>The exit block of a loop (what runs after the loop condition is false).</summary>
    LoopExit,
}

/// <summary>
/// A basic block in the Control Flow Graph. Contains a maximal sequence of
/// statements with a single entry point and a single exit point (or
/// control flow divergence at the end).
/// </summary>
public class CFGBlock
{
    /// <summary>
    /// Block name — stable across round-trips. For MainBlock, this is "#MainBlock".
    /// For named blocks, this is the user-defined name (e.g., "LoopCond", "HandleIncPointer").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The structural type of this block.
    /// </summary>
    public CFGBlockType Type { get; set; } = CFGBlockType.Basic;

    /// <summary>
    /// Ordered statements within this block. All statements are flat —
    /// no nested calls, all expressions are either simple or PubVar assignments.
    /// </summary>
    public List<CFGStatement> Statements { get; set; } = [];

    /// <summary>
    /// Typed edges from this block to its successors.
    /// A block can have multiple successors (e.g., BranchHeader → True/False).
    /// </summary>
    public List<CFGEdge> Successors { get; set; } = [];

    /// <summary>
    /// For sequential fall-through (NextBlock = "..."), the name of the next block.
    /// Null if this block ends with a control flow statement (Branch/Loop/ToLoopCond/Break)
    /// or has no successor.
    /// </summary>
    public string? NextBlockName { get; set; }

    /// <summary>
    /// For loop body blocks, the name of the parent loop header block.
    /// Used to generate correct ToLoopCond arguments.
    /// </summary>
    public string? ParentLoopBlockName { get; set; }

    /// <summary>
    /// Whether this block is the MainBlock (entry point).
    /// </summary>
    public bool IsMainBlock => Type == CFGBlockType.Entry;

    /// <summary>
    /// Whether this block ends with a control flow statement
    /// (Branch, Loop, ToLoopCond, Break, Return).
    /// </summary>
    public bool EndsWithControlFlow =>
        Statements.Count > 0 && Statements[^1] is { Kind: CFGStatementKind.Branch or CFGStatementKind.Loop
            or CFGStatementKind.ToLoopCond or CFGStatementKind.Break };
}