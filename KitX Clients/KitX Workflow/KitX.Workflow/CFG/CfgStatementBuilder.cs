using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Models.Statements;
using KitX.Workflow.Models;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.CFG;

/// <summary>
/// Single construction point for <see cref="CFGStatement"/>. Absorbs the cross-cutting
/// bookkeeping that was previously scattered across BS2CFG and BP2CFG (StatementId minting,
/// Fingerprint derivation, OriginalExpression rendering, PubVarNames tracking).
/// </summary>
/// <remarks>
/// <para>v5.0: <see cref="CFGStatementKind"/> has been eliminated. FlowControlShape (non-null
/// for control-flow) and PubVarTarget (non-null for assignments) are the authoritative
/// discriminants. See <see cref="IFlowControlFunctionDefinition"/> for the contract.</para>
/// <para>Caller sets the semantic fields; <see cref="Build"/> derives fingerprint and
/// OriginalExpression once — no post-processing patches.</para>
/// </remarks>
public class CfgStatementBuilder
{
    // ── Required ──────────────────────────────────────────────────────
    public required string BlockName { get; set; }

    // ── Semantic fields (set by caller) ───────────────────────────────
    public FlowControlType? FlowControlShape { get; set; }
    public string? FunctionName { get; set; }
    public string? FullFunctionName { get; set; }
    public List<string> Arguments { get; set; } = [];
    public string? PubVarTarget { get; set; }
    public List<BranchArm> Arms { get; set; } = [];
    public string? ConditionExpression { get; set; }

    // ── Metadata (optional) ───────────────────────────────────────────
    public string? StatementId { get; set; }
    public string? Comment { get; set; }
    public int SourceLine { get; set; }

    // ── Source text override ──────────────────────────────────────────
    /// <summary>Explicit source text used for the OriginalExpression—set for flow-control
    /// and parsed statements. Omit for pipeline or auto-generated statements.</summary>
    public string? SourceText { get; set; }

    // ── Construction ──────────────────────────────────────────────────

    public CFGStatement Build(ICollection<string>? pubVarNames = null)
    {
        if (pubVarNames != null && !string.IsNullOrEmpty(PubVarTarget))
            pubVarNames.Add(PubVarTarget);

        var fingerprint = DeriveFingerprint();
        var originalExpression = SourceText ?? RenderDefault();

        return new CFGStatement
        {
            StatementId = string.IsNullOrEmpty(StatementId) ? Guid.NewGuid().ToString() : StatementId!,
            BlockName = BlockName,
            FlowControlShape = FlowControlShape,
            FunctionName = FunctionName,
            FullFunctionName = FullFunctionName,
            Arguments = Arguments,
            PubVarTarget = PubVarTarget,
            Arms = Arms,
            ConditionExpression = ConditionExpression,
            Fingerprint = fingerprint,
            OriginalExpression = originalExpression,
            Comment = Comment,
            SourceLine = SourceLine
        };
    }

    // v5.0: Fingerprint is only meaningful for non-control-flow statements with a function name.
    // Control-flow statements are never reused (each is unique by its block position).
    private string? DeriveFingerprint()
    {
        if (FlowControlShape != null) return null;
        if (string.IsNullOrEmpty(FunctionName)) return null;
        return ExprUtils.ComputeFingerprint(FunctionName!, Arguments);
    }

    // v5.0: Render text from FlowControlShape or FunctionName + PubVarTarget.
    // Control-flow uses the flow-control function's RenderSource (from the registry);
    // value-producing functions use pipeline form.
    private string RenderDefault()
    {
        // Flow-control: render via the builtin registry's flow-control function.
        if (FlowControlShape != null)
        {
            var fcDef = BuiltinFunctionRegistry.Instance.AllDefinitions
                .OfType<IFlowControlFunctionDefinition>()
                .FirstOrDefault(f => f.FlowControlShape == FlowControlShape);
            if (fcDef != null)
                return fcDef.RenderSource(ConditionExpression, Arms, Arguments);
            return $"{FlowControlShape}(...)";
        }

        // Pure assignment (Expr > var tap) — no function call, just the source expression.
        if (string.IsNullOrEmpty(FunctionName))
        {
            var rhs = Arguments.Count > 0 ? Arguments[0] : "null";
            return !string.IsNullOrEmpty(PubVarTarget) ? $"{rhs} > {PubVarTarget}" : rhs;
        }

        // Function call: render as args > Func(args) or Func(args) > target.
        var callText = $"{FunctionName}({string.Join(", ", Arguments)})";
        if (!string.IsNullOrEmpty(PubVarTarget))
            return $"{callText} > {PubVarTarget}";
        return callText;
    }
}
