namespace KitX.Workflow.CFG;

using KitX.Core.Contract.Workflow;

/// <summary>
/// Kinds of statements in the CFG. Unified statement kind replacing the former Pipeline.FormattedStatementKind.
/// </summary>
public enum CFGStatementKind
{
    /// <summary>Unknown or unclassified</summary>
    Unknown,

    /// <summary>Print(expr)</summary>
    Print,

    /// <summary>Pause(ms)</summary>
    Pause,

    /// <summary>Set("varName", expr)</summary>
    Set,

    /// <summary>Get("varName") — standalone or as part of a PubVar assignment</summary>
    Get,

    /// <summary>pubVar = FunctionCall(args...)</summary>
    Assignment,

    /// <summary>Branch(condition, trueBlock, falseBlock)</summary>
    Branch,

    /// <summary>Loop(condition, loopBody, afterLoop)</summary>
    Loop,

    /// <summary>Switch(selector, defaultBlock, b0, b1, ...) — N-way dispatch by integer index</summary>
    Switch,

    /// <summary>ToLoopCond("parentBlock")</summary>
    ToLoopCond,

    /// <summary>Break()</summary>
    Break,

    /// <summary>NextBlock = ... (handled internally, no node created)</summary>
    NextBlockAssignment,

    /// <summary>Plain expression without assignment</summary>
    Expression,

    /// <summary>PluginCallWithTarget(pluginName, methodName, targetDevice, args...) — cross-device plugin call</summary>
    PluginCallWithTarget,

    /// <summary>TryGetDevice(deviceSearchPattern) — returns DeviceInfo or null</summary>
    TryGetDevice,
}

/// <summary> within a CFG block. All expressions are flat —
/// nested calls have been expanded into sequential PubVar assignments.
/// Carries all information needed to produce a Blueprint node or a
/// BlockScript statement.
/// </summary>
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
    /// What kind of statement this is.
    /// </summary>
    public CFGStatementKind Kind { get; set; }

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
    /// May be a PubVar name or a complex expression. For Switch this is the integer selector.
    /// </summary>
    public string? ConditionExpression { get; set; }

    /// <summary>
    /// The PubVar holding the condition result, if pre-computed.
    /// </summary>
    public string? ConditionPubVar { get; set; }

    /// <summary>
    /// Shared control-flow arms model (Arms / TrueBlockName / FalseBlockName / LoopbackTarget).
    /// Embedded once here instead of duplicating the accessors and SetArm across CFGStatement
    /// and FlowControlStatement. The delegating properties below keep the public surface
    /// (<c>stmt.Arms</c>, <c>stmt.TrueBlockName</c>, ...) unchanged.
    /// </summary>
    private readonly ControlFlowArms _controlFlowArms = new();

    /// <summary>
    /// Outgoing arms of this control-flow statement. Generalised model replacing the former
    /// fixed <c>TrueBlockName</c>/<c>FalseBlockName</c>/<c>ToLoopCondReturnTo</c> triple.
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

    // --- Metadata ---
    /// <summary>
    /// True if this statement was inserted as a Loop condition duplication
    /// before a ToLoopCond statement. These are not present in the original
    /// script but are needed for correct loop execution semantics.
    /// </summary>
    public bool IsLoopConditionDuplication { get; set; }

    /// <summary>
    /// Expression fingerprint for PubVar reuse detection.
    /// Same expression → same fingerprint → nodes can be shared.
    /// </summary>
    public string? Fingerprint { get; set; }
}