using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.Pipeline;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions;

/// <summary>
/// Set 内置函数 — 设置变量值。
/// </summary>
public class SetFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "Set";
    public string DisplayName => "Set";
    public bool IsFlowControl => false;
    public bool IsNonExtractable => true;
    public BlueprintNodeType? LegacyNodeType => BlueprintNodeType.Set;
    public double NodeWidth => 120;
    public double NodeHeight => 60;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 20),
        new("Value", PinType.Any, 40)
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [
        new("Exec", PinType.Execution, 20)
    ];

    public string? GetExecutionMethodBody() => null;

    public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

    public List<FormattedStatement> FormatInvocation(
        InvocationExpressionSyntax invoke, string blockName,
        PipelineContext context, string? assignedVar)
    {
        var currentArgExprs = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();

        // First arg is varName (string literal) — extract it
        string? setVarName = null;
        if (invoke.ArgumentList.Arguments.Count > 0)
        {
            var firstArgExpr = invoke.ArgumentList.Arguments[0].Expression;
            var varNameLiteral = ExprUtils.GetStringLiteralValue(firstArgExpr);
            setVarName = varNameLiteral ?? currentArgExprs[0];
            currentArgExprs.RemoveAt(0);
        }

        return [new FormattedStatement
        {
            BlockName = blockName,
            Kind = FormattedStatementKind.Set,
            FunctionName = FunctionName,
            SetVarName = setVarName,
            Arguments = currentArgExprs,
            OriginalExpression = invoke.ToString(),
            SourceLine = 0,
        }];
    }

    public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt)
    {
        if (node is BuiltinFunctionNode bfn)
            bfn.Properties["VarName"] = stmt.SetVarName ?? "";
        return node;
    }

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var varName = node switch
        {
            SetNode sn => sn.VarName,
            BuiltinFunctionNode bfn => bfn.Properties.GetValueOrDefault("VarName", ""),
            _ => ""
        };
        var value = helper.GetInputValue(node, "Value");
        return new ExpressionStatement
        {
            Expression = $"{FunctionName}(\"{varName}\", {value})",
            SourceCode = $"{FunctionName}(\"{varName}\", {value});",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}
