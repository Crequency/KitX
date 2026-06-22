namespace KitX.Workflow.CFG;

/// <summary>
/// Type of a CFG block, indicating its structural role in the control flow.
/// </summary>
/// <remarks>
/// v5.0 removed <c>LoopHeader</c>/<c>LoopBody</c>: ForLoop's body is an ordinary block that
/// re-enters the loop via Goto, and the loop header is just the block holding the ForLoop
/// statement. The CFG no longer needs dedicated loop-block types.
/// </remarks>
public enum CFGBlockType
{
    /// <summary>MainBlock — the entry point of the script.</summary>
    Entry,

    /// <summary>A plain sequential block with no control flow divergence at the end.</summary>
    Basic,

    /// <summary>A block ending with a Branch statement (condition → true/false targets).</summary>
    BranchHeader,
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
    /// Sequential fall-through is represented as a <see cref="CFGEdgeType.Sequential"/>
    /// edge here — the single source of truth for "what runs next" — instead of a
    /// parallel <c>NextBlockName</c> field that could drift out of sync.
    /// </summary>
    public List<CFGEdge> Successors { get; set; } = [];

    /// <summary>
    /// The sequential fall-through target: the <c>ToBlockName</c> of the
    /// <see cref="CFGEdgeType.Sequential"/> edge in <see cref="Successors"/>, or null.
    /// Equivalent to the former standalone <c>NextBlockName</c> field, now derived from
    /// <see cref="Successors"/> so there is a single source of truth. Non-null only for
    /// blocks that do not <see cref="EndsWithControlFlow"/> (control-flow blocks have no
    /// Sequential edge).
    /// </summary>
    public string? FallThroughTarget =>
        Successors.FirstOrDefault(e => e.Type == CFGEdgeType.Sequential)?.ToBlockName;

    /// <summary>
    /// For loop body blocks, the name of the parent loop header block.
    /// Used to generate correct ToLoopCond arguments. v5.0 transition: retained during migration.
    /// </summary>
    public string? ParentLoopBlockName { get; set; }

    /// <summary>
    /// Whether this block is the MainBlock (entry point).
    /// </summary>
    public bool IsMainBlock => Type == CFGBlockType.Entry;

    /// <summary>
    /// Whether this block ends with a control flow statement
    /// (any non-null FlowControlShape: ConditionalJump, IterativeJump/IterativeCounted,
    /// UnconditionalJump, IndexedDispatch, LoopBackedge, LoopExit, ScriptReturn).
    /// </summary>
    public bool EndsWithControlFlow =>
        Statements.Count > 0 && Statements[^1].FlowControlShape != null;

    /// <summary>
    /// The effective statement sequence consumers should iterate. PipelineStatement entries are
    /// transparently expanded to their <see cref="PipelineStatement.FlattenedStatements"/> view,
    /// so CFG2BP/CFG2CS/executor see only plain CFGStatements. Non-pipeline statements pass through.
    /// </summary>
    /// <remarks>
    /// Use this instead of <see cref="Statements"/> whenever the consumer treats each entry as an
    /// executable step (node creation, CS emission, execution). <see cref="Statements"/> remains
    /// the authoritative storage (BS2CFG writes PipelineStatement directly into it).
    /// </remarks>
    public IEnumerable<CFGStatement> GetEffectiveStatements()
    {
        foreach (var s in Statements)
        {
            if (s is PipelineStatement ps)
            {
                foreach (var f in ps.FlattenedStatements)
                    yield return f;
            }
            else
            {
                yield return s;
            }
        }
    }
}