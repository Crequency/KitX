namespace KitX.Core.Workflow.CFG;

/// <summary>
/// Type of a control flow edge between blocks. Making edge semantics
/// explicit eliminates the need for heuristic-based control flow resolution.
/// </summary>
public enum CFGEdgeType
{
    /// <summary>
    /// Sequential fall-through (NextBlock assignment).
    /// The current block ends normally and transfers to the next block.
    /// </summary>
    Sequential,

    /// <summary>
    /// Branch condition evaluates to true.
    /// Maps to Blueprint's True output pin on a Branch node.
    /// </summary>
    BranchTrue,

    /// <summary>
    /// Branch condition evaluates to false.
    /// Maps to Blueprint's False output pin on a Branch node.
    /// </summary>
    BranchFalse,

    /// <summary>
    /// Loop condition evaluates to true — enter loop body.
    /// Maps to Blueprint's LoopBody output pin on a Loop node.
    /// </summary>
    LoopBody,

    /// <summary>
    /// Loop condition evaluates to false — exit loop.
    /// Maps to Blueprint's LoopEnd output pin on a Loop node.
    /// </summary>
    LoopExit,

    /// <summary>
    /// Return to the loop condition block from the loop body (ToLoopCond).
    /// This is a back-edge in the CFG that doesn't map to a single connection
    /// but represents the loop's iterative structure.
    /// </summary>
    LoopbackToCondition,

    /// <summary>
    /// Break from the current loop — exits to the loop's exit block.
    /// </summary>
    Break,
}

/// <summary>
/// A typed edge in the Control Flow Graph, capturing the semantics
/// of the transition between two blocks.
/// </summary>
public class CFGEdge
{
    /// <summary>
    /// The block this edge originates from.
    /// </summary>
    public required string FromBlockName { get; set; }

    /// <summary>
    /// The block this edge targets.
    /// </summary>
    public required string ToBlockName { get; set; }

    /// <summary>
    /// The semantic type of this edge.
    /// </summary>
    public required CFGEdgeType Type { get; set; }

    /// <summary>
    /// The corresponding Blueprint pin name (e.g., "True", "False", "LoopBody", "LoopEnd", "Exec").
    /// Null for edges that don't map to a specific pin (e.g., LoopbackToCondition).
    /// </summary>
    public string? PinName { get; set; }
}