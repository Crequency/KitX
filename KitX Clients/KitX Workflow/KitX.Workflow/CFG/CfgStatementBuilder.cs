using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Models.Statements;
using KitX.Workflow.Models;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.CFG;

/// <summary>
/// Single construction point for <see cref="CFGStatement"/>. Absorbs cross-cutting
/// bookkeeping (StatementId, Fingerprint, OriginalExpression) that was previously
/// scattered across BS2CFG and BP2CFG.
/// </summary>
/// <remarks>
/// v5.0: <see cref="CFGStatementKind"/> and <see cref="FlowControlType"/> eliminated.
/// Flow-control statements are identified by <c>FunctionName</c> lookup via the registry.
/// </remarks>
public class CfgStatementBuilder
{
    public required string BlockName { get; set; }
    public string? FunctionName { get; set; }
    public string? FullFunctionName { get; set; }
    public List<string> Arguments { get; set; } = [];
    public string? PubVarTarget { get; set; }
    public List<BranchArm> Arms { get; set; } = [];
    public string? ConditionExpression { get; set; }
    public string? StatementId { get; set; }
    public string? Comment { get; set; }
    public int SourceLine { get; set; }
    public string? SourceText { get; set; }

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

    private string? DeriveFingerprint()
    {
        // Look up the function in the registry; flow-control functions are never reused.
        if (!string.IsNullOrEmpty(FunctionName)
            && BuiltinFunctionRegistry.Instance.Get(FunctionName) is { ArgLayout: not null })
            return null;
        if (string.IsNullOrEmpty(FunctionName)) return null;
        return ExprUtils.ComputeFingerprint(FunctionName!, Arguments);
    }

    private string RenderDefault()
    {
        // Flow-control: lookup via registry and call RenderSource.
        if (!string.IsNullOrEmpty(FunctionName)
            && BuiltinFunctionRegistry.Instance.Get(FunctionName) is { ArgLayout: not null } fcDef)
            return fcDef.RenderSource(ConditionExpression, Arms, Arguments);

        // Pure assignment (Expr > var tap).
        if (string.IsNullOrEmpty(FunctionName))
        {
            var rhs = Arguments.Count > 0 ? Arguments[0] : "null";
            return !string.IsNullOrEmpty(PubVarTarget) ? $"{rhs} > {PubVarTarget}" : rhs;
        }

        // Function call: render as Func(args) or Func(args) > target.
        var callText = $"{FunctionName}({string.Join(", ", Arguments)})";
        if (!string.IsNullOrEmpty(PubVarTarget))
            return $"{callText} > {PubVarTarget}";
        return callText;
    }
}
