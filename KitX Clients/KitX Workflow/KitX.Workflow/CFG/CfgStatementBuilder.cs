using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.CFG;

/// <summary>
/// Single construction point for <see cref="CFGStatement"/>. Absorbs the cross-cutting
/// bookkeeping that was previously scattered across BS2CFG and BP2CFG (StatementId minting,
/// Fingerprint derivation, Kind derivation, OriginalExpression rendering, PubVarNames tracking).
/// </summary>
/// <remarks>
/// <para><b>Design intent (architecture refactor phase 1):</b> eliminate the "post-processing
/// patch" anti-pattern where converters built partial statements then looped back to fill
/// cross-cutting fields. All derived/cache fields are computed once here in <see cref="Build"/>.</para>
/// <para><b>Field classification:</b>
/// <list type="bullet">
///   <item>Structural (authoritative, set by caller): BlockName, FlowControlShape, FunctionName,
///   FullFunctionName, Arguments, PubVarTarget, ConditionExpression, Arms.</item>
///   <item>Metadata (optional, set by caller): StatementId, PipelineId, PipelineSegmentIndex,
///   Comment, SourceLine.</item>
///   <item>Source text (caller-supplied when known verbatim, else auto-rendered): SourceText.</item>
///   <item>Derived (computed by Build, never set by caller): Kind, Fingerprint, OriginalExpression.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class CfgStatementBuilder
{
    // ─── Structural fields ───────────────────────────

    /// <summary>The block this statement belongs to. Mandatory.</summary>
    public required string BlockName { get; set; }

    /// <summary>Control-flow shape (null for non-control-flow). Authoritative for Kind derivation.</summary>
    public FlowControlType? FlowControlShape { get; set; }

    /// <summary>Function name (e.g. "HelperFuncCompare", "Print"). Null for pure assignments.</summary>
    public string? FunctionName { get; set; }

    /// <summary>Full dotted method path for plugin calls. Null for builtins/helpers.</summary>
    public string? FullFunctionName { get; set; }

    /// <summary>Resolved argument strings (literals, PubVar names, ConstBlock names).</summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>The PubVar being assigned, if this is an assignment.</summary>
    public string? PubVarTarget { get; set; }

    /// <summary>Condition/selector expression for flow-control statements.</summary>
    public string? ConditionExpression { get; set; }

    /// <summary>
    /// The PubVar holding the condition result, if pre-computed. Transitional (CFG2CSConverter
    /// still reads it to force bool typing on Branch conditions). TODO(phase-2/4): retire once
    /// the type inferencer derives bool-ness from the ConditionalJump shape.
    /// </summary>
    public string? ConditionPubVar { get; set; }

    /// <summary>Control-flow arms (Branch true/false, Switch cases, Goto target).</summary>
    public List<BranchArm> Arms { get; set; } = [];

    // ─── Metadata ────────────────────────────────────

    /// <summary>Caller-supplied statement id; null → Build mints a fresh Guid.</summary>
    public string? StatementId { get; set; }

    /// <summary>Pipeline id for round-trip grouping; null for non-pipeline statements.</summary>
    public string? PipelineId { get; set; }

    /// <summary>Ordinal within the pipeline (-1 for non-pipeline).</summary>
    public int PipelineSegmentIndex { get; set; } = -1;

    /// <summary>Comment anchored to this statement (v5.0 §9 bidirectional retention).</summary>
    public string? Comment { get; set; }

    /// <summary>Source line number.</summary>
    public int SourceLine { get; set; }

    // ─── Source text ─────────────────────────────────

    /// <summary>
    /// Verbatim source text for <see cref="CFGStatement.OriginalExpression"/>. When non-null,
    /// used as-is (caller knows the exact rendering, e.g. flow-control SourceCode, pipeline
    /// verbatim text, or <c>invoke.SourceText</c>). When null, Build auto-renders from the
    /// structural fields via <see cref="RenderDefault"/>.
    /// </summary>
    public string? SourceText { get; set; }

    // ─── Build ───────────────────────────────────────

    /// <summary>
    /// Constructs the immutable <see cref="CFGStatement"/>, deriving Kind/Fingerprint/
    /// OriginalExpression/StatementId and (optionally) tracking the PubVarTarget in
    /// <paramref name="pubVarNames"/> for downstream resolution.
    /// </summary>
    public CFGStatement Build(ICollection<string>? pubVarNames = null)
    {
        // Track the PubVar target before anything else so even auto-named temps are visible
        // to IsVariableName checks that run later in the same FormatPipeline pass.
        if (!string.IsNullOrEmpty(PubVarTarget) && pubVarNames != null && !pubVarNames.Contains(PubVarTarget))
            pubVarNames.Add(PubVarTarget);

        var kind = DeriveKind();
        var fingerprint = DeriveFingerprint(kind);
        var originalExpression = SourceText ?? RenderDefault(kind);

        return new CFGStatement
        {
            StatementId = string.IsNullOrEmpty(StatementId) ? Guid.NewGuid().ToString() : StatementId!,
            BlockName = BlockName,
            Kind = kind,
            FlowControlShape = FlowControlShape,
            FunctionName = FunctionName,
            FullFunctionName = FullFunctionName,
            Arguments = Arguments,
            PubVarTarget = PubVarTarget,
            ConditionExpression = ConditionExpression,
            ConditionPubVar = ConditionPubVar,
            Arms = Arms,
            OriginalExpression = originalExpression,
            Fingerprint = fingerprint,
            PipelineId = PipelineId,
            PipelineSegmentIndex = PipelineSegmentIndex,
            Comment = Comment,
            SourceLine = SourceLine,
        };
    }

    /// <summary>
    /// Derives the Kind from FlowControlShape (authoritative for control flow) or from the
    /// presence of PubVarTarget (Assignment) vs absence (Expression). Mirrors the mapping in
    /// <see cref="IBuiltinFunctionDefinition.StatementKind"/> for the control-flow shapes.
    /// </summary>
    private CFGStatementKind DeriveKind() => FlowControlShape switch
    {
        FlowControlType.ConditionalJump => CFGStatementKind.Branch,
        FlowControlType.IterativeCounted => CFGStatementKind.ForLoop,
        FlowControlType.UnconditionalJump => CFGStatementKind.Goto,
        FlowControlType.IndexedDispatch => CFGStatementKind.Switch,
        FlowControlType.LoopExit => CFGStatementKind.Break,
        null => !string.IsNullOrEmpty(PubVarTarget)
            ? CFGStatementKind.Assignment
            : CFGStatementKind.Expression,
        _ => CFGStatementKind.Expression
    };

    /// <summary>
    /// Computes the fingerprint for value-carrying statements (Assignment/Expression with a
    /// non-null FunctionName). Control-flow and pure-assignment (FunctionName null) statements
    /// get no fingerprint — they are never reused.
    /// </summary>
    private string? DeriveFingerprint(CFGStatementKind kind)
    {
        if (kind is not (CFGStatementKind.Assignment or CFGStatementKind.Expression)) return null;
        if (string.IsNullOrEmpty(FunctionName)) return null;
        return ExprUtils.ComputeFingerprint(FunctionName!, Arguments);
    }

    /// <summary>
    /// Default rendering when the caller did not supply <see cref="SourceText"/>. Produces a
    /// best-effort canonical text from the structural fields. Callers that know the exact
    /// source rendering (flow-control SourceCode, verbatim pipeline text, invoke.SourceText)
    /// should set <see cref="SourceText"/> explicitly to preserve round-trip fidelity.
    /// </summary>
    private string RenderDefault(CFGStatementKind kind)
    {
        // Flow-control: defer to the arms model's canonical renderer. Goto's loopback target
        // lives in Arms[0] (IsLoopback=true); surface it as the renderer's loopbackTarget arg.
        if (FlowControlShape != null)
        {
            var loopback = Arms.FirstOrDefault(a => a.IsLoopback)?.TargetBlockName;
            return FlowControlStatement.RenderSource(
                FlowControlShape.Value, ConditionExpression ?? string.Empty, Arms, loopback);
        }

        // Pure assignment (Expr > var tap): render the pipeline form when FunctionName is null.
        if (kind == CFGStatementKind.Assignment && string.IsNullOrEmpty(FunctionName))
        {
            var rhs = Arguments.Count > 0 ? Arguments[0] : "null";
            return $"{rhs} > {PubVarTarget}";
        }

        // Function call: render as [pubVar = ]Func(args...).
        var call = $"{FunctionName}({string.Join(", ", Arguments)})";
        return kind == CFGStatementKind.Assignment && !string.IsNullOrEmpty(PubVarTarget)
            ? $"{PubVarTarget} = {call}"
            : call;
    }
}
