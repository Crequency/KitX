namespace KitX.Workflow.CFG;

using KitX.Core.Contract.Workflow;

/// <summary>
/// A statement within a CFG block. All expressions are flat —
/// nested calls have been expanded into sequential PubVar assignments.
/// Carries all information needed to produce a Blueprint node or a
/// BlockScript statement.
/// </summary>
/// <remarks>
/// v5.0: <see cref="CFGStatementKind"/> has been eliminated.
/// FlowControlShape (non-null = flow-control) and PubVarTarget (non-null = assignment)
/// are the authoritative discriminants. Kind was a redundant denormalisation.
/// </remarks>
public class CFGStatement
{
    /// <summary>
    /// Unique identifier for this statement. Used to link CFG statements
    /// to Blueprint nodes during conversion.
    /// </summary>
    public string StatementId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The block this statement belongs to.
    /// </summary>
    public string BlockName { get; set; } = string.Empty;

    /// <summary>
    /// True if this statement terminates the current block (no fall-through).
    /// Set by CfgStatementBuilder from the function definition's IsBlockTerminator.
    /// </summary>
    public bool IsBlockTerminator { get; set; }

    /// <summary>
    /// The original source expression for this statement.
    /// Used for BlockScript serialization and debugging.
    /// </summary>
    public string OriginalExpression { get; set; } = string.Empty;

    /// <summary>
    /// Source line number in the original script.
    /// </summary>
    public int SourceLine { get; set; }

    // --- For Assignment / Get / Set ---
    /// <summary>
    /// The PubVar being assigned (e.g. "vaaa0001"), if this is an assignment.
    /// </summary>
    public string? PubVarTarget { get; set; }

    // --- For function calls ---
    /// <summary>
    /// Function name (e.g. "HelperFuncCompare", "Get", "Set", "Print").
    /// For plugin calls, this is the short name.
    /// </summary>
    public string? FunctionName { get; set; }

    /// <summary>
    /// Full dotted method path for plugin/external calls
    /// (e.g. "TestPlugin.WPF.Core.HelloKitX").
    /// Null for built-in and helper functions.
    /// </summary>
    public string? FullFunctionName { get; set; }

    /// <summary>
    /// Raw argument strings after expansion (no nested calls).
    /// Each argument is either a literal, a PubVar name, a ConstBlock variable name,
    /// or Get("varName").
    /// </summary>
    public List<string> Arguments { get; set; } = [];

    // --- For flow control ---
    /// <summary>
    /// The condition expression for Branch/Loop/Switch statements.
    /// Post-expansion this is a single PubVar identifier (the source of the condition value);
    /// consumers (EmitStatements, DataEdgeBuilder, InferPubVarTypes) treat it as the condition
    /// source. For Switch this is the integer selector. v5.0 ForLoop has no condition expression
    /// (its condition is internalized in the node).
    /// </summary>
    public string? ConditionExpression { get; set; }

    /// <summary>
    /// Shared control-flow arms model (Arms / TrueBlockName / FalseBlockName).
    /// v5.0: LoopbackTarget removed (ToLoopCond deleted). Goto uses Arms[0] (Exec arm).
    /// </summary>
    private readonly ControlFlowArms _controlFlowArms = new();

    /// <summary>
    /// Outgoing arms of this control-flow statement.
    /// See <see cref="BranchArm"/> for the per-arm layout of each control-flow kind.
    /// </summary>
    public List<BranchArm> Arms
    {
        get => _controlFlowArms.Arms;
        set => _controlFlowArms.Arms = value;
    }

    /// <summary>Convenience accessor: the true-branch / loop-body target (Arms[0]).</summary>
    public string? TrueBlockName
    {
        get => _controlFlowArms.TrueBlockName;
        set => _controlFlowArms.TrueBlockName = value;
    }

    /// <summary>Convenience accessor: the false-branch / loop-exit target (Arms[1]).</summary>
    public string? FalseBlockName
    {
        get => _controlFlowArms.FalseBlockName;
        set => _controlFlowArms.FalseBlockName = value;
    }

    // --- Metadata ---

    /// <summary>
    /// Expression fingerprint for PubVar reuse detection.
    /// Same expression → same fingerprint → nodes can be shared.
    /// </summary>
    public string? Fingerprint { get; set; }

    /// <summary>
    /// Comment attached to this statement (v5.0 bidirectional comment retention, §9).
    /// For pipelines, the leading comment anchors to the PipelineStatement. Round-trips
    /// BS→CFG→BS and BS→BP→BS.
    /// </summary>
    public string? Comment { get; set; }
}