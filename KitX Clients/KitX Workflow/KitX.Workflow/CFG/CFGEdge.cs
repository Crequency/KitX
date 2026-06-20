namespace KitX.Workflow.CFG;

/// <summary>
/// Type of a control flow edge between blocks. Making edge semantics
/// explicit eliminates the need for heuristic-based control flow resolution.
///
/// <para><b>Consumption model.</b> The active consumers are:
/// <list type="bullet">
/// <item><see cref="Sequential"/> — read via <see cref="CFGBlock.FallThroughTarget"/>
///   by CFG2BS / CFG2BP / CFG2CS / PipelineAssembler (the sole fall-through truth source,
///   replacing the former parallel <c>NextBlockName</c> field).</item>
/// <item><see cref="LoopBody"/> / <see cref="LoopbackToCondition"/> — read by
///   BP2CFGConverter.SetParentLoopReferences and CFGConditionDuplicator to resolve the
///   parent loop of a body block and to duplicate loop conditions before back-edges.</item>
/// </list></para>
/// <para><see cref="BranchTrue"/>, <see cref="BranchFalse"/>, <see cref="LoopExit"/>,
/// <see cref="Break"/>, <see cref="Switch"/> are produced by GetEdgeType from
/// (statement Kind, arm PinName) and serve as descriptive edge metadata. Their
/// control-flow resolution is driven by the statement's <c>Arms</c> + the builtin's
/// <c>EmitStatements</c> (which read arms directly), so a parallel edge.Type-driven
/// consumer would duplicate that resolution rather than replace a broken one. They are
/// kept for observability and future edge-walking consumers; they are not "dead" in the
/// sense of being wrong, only not-yet-required as an authority.</para>
/// </summary>
public enum CFGEdgeType
{
    /// <summary>
    /// Sequential fall-through (NextBlock assignment).
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