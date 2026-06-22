namespace KitX.Workflow.CFG;

/// <summary>
/// Type of a control flow edge between blocks. Making edge semantics
/// explicit eliminates the need for heuristic-based control flow resolution.
/// </summary>
/// <remarks>
/// v5.0 transition: <c>LoopbackToCondition</c> remains during the staged migration (removed
/// with ToLoopCond/CFGConditionDuplicator in layer 6). v5.0 Goto uses <see cref="Sequential"/>.
/// </remarks>
public enum CFGEdgeType
{
    /// <summary>
    /// Sequential fall-through (NextBlock assignment / v5.0 Goto).
    /// The current block ends normally and transfers to the next block.
    /// Consumed via <see cref="CFGBlock.FallThroughTarget"/>.
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
    /// Loop/ForLoop condition evaluates to true — enter loop body.
    /// Maps to Blueprint's LoopBody output pin.
    /// </summary>
    LoopBody,

    /// <summary>
    /// Loop/ForLoop condition evaluates to false — exit loop.
    /// Maps to Blueprint's LoopEnd output pin.
    /// </summary>
    LoopExit,

    /// <summary>
    /// Return to the loop condition block from the loop body (ToLoopCond).
    /// v5.0 transition: retained during migration; Goto uses Sequential instead.
    /// </summary>
    LoopbackToCondition,

    /// <summary>
    /// Break from the current loop — exits to the loop's exit block.
    /// </summary>
    Break,

    /// <summary>
    /// Switch arm taken by integer selector. <see cref="CFGEdge.PinName"/> carries the arm
    /// index (<c>"Default"</c> or <c>"0"</c>..<c>"N-1"</c>).
    /// </summary>
    Switch,
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