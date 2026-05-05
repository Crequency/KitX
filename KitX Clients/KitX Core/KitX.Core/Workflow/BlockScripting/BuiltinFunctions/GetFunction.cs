using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions;

/// <summary>
/// Get 内置函数 — 读取变量值。
/// </summary>
public class GetFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "Get";
    public string DisplayName => "Get";
    public bool IsFlowControl => false;
    public bool IsNonExtractable => false;
    public BlueprintNodeType? LegacyNodeType => BlueprintNodeType.Get;
    public CFGStatementKind StatementKind => CFGStatementKind.Assignment;
    public double NodeWidth => 120;
    public double NodeHeight => 60;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 20)
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [
        new("Exec", PinType.Execution, 20),
        new("Value", PinType.Any, 40)
    ];


    public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

    public List<CFGStatement> FormatInvocation(
        InvocationExpressionSyntax invoke, string blockName,
        PipelineContext context, string? assignedVar)
    {
        var args = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();

        // Extract varName from first argument
        string? getVarName = null;
        if (invoke.ArgumentList.Arguments.Count > 0)
        {
            var firstArgExpr = invoke.ArgumentList.Arguments[0].Expression;
            getVarName = ExprUtils.GetStringLiteralValue(firstArgExpr) ?? args[0];
        }

        // Auto-generate PubVar if not already assigned
        string? pubVarTarget;
        if (string.IsNullOrEmpty(assignedVar) || !context.PubVarNames.Contains(assignedVar))
        {
            pubVarTarget = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
            if (!context.PubVarNames.Contains(pubVarTarget))
                context.PubVarNames.Add(pubVarTarget);
        }
        else
        {
            pubVarTarget = assignedVar;
        }

        return [new CFGStatement
        {
            BlockName = blockName,
            Kind = CFGStatementKind.Assignment,
            FunctionName = FunctionName,
            PubVarTarget = pubVarTarget,
            GetVarName = getVarName,
            Arguments = args,
            OriginalExpression = invoke.ToString(),
            SourceLine = 0,
        }];
    }

    public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
    {
        var varName = stmt.GetVarName ?? (stmt.Arguments?.Count > 0 ? stmt.Arguments[0].Trim('"') : "");
        if (node is GetNode gn)
            gn.VarName = varName;
        else if (node is BuiltinFunctionNode bfn)
            bfn.Properties["VarName"] = varName;
        return node;
    }

    public (string?, string?, string?) ExtractStatementFields(
        InvocationExpressionSyntax invoke, List<string> expandedArgs,
        string? assignedVar, PipelineContext context)
    {
        string? getVarName = null;
        if (expandedArgs.Count > 0)
        {
            var firstArgExpr = invoke.ArgumentList.Arguments[0].Expression;
            getVarName = ExprUtils.GetStringLiteralValue(firstArgExpr) ?? expandedArgs[0];
        }

        string? pubVarTarget;
        if (string.IsNullOrEmpty(assignedVar) || !context.PubVarNames.Contains(assignedVar))
        {
            pubVarTarget = ExprUtils.GeneratePubVarName(context.NextPubVarCounter++);
            if (!context.PubVarNames.Contains(pubVarTarget))
                context.PubVarNames.Add(pubVarTarget);
        }
        else
        {
            pubVarTarget = assignedVar;
        }

        return (null, getVarName, pubVarTarget);
    }

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var varName = node switch
        {
            GetNode gn => gn.VarName,
            BuiltinFunctionNode bfn => bfn.Properties.GetValueOrDefault("VarName", ""),
            _ => ""
        };
        return new ExpressionStatement
        {
            Expression = $"Get(\"{varName}\")",
            SourceCode = $"Get(\"{varName}\");",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}
