using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.Pipeline;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions;

/// <summary>
/// Print 内置函数 — 输出值到控制台。
/// </summary>
public class PrintFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "Print";
    public string DisplayName => "Print";
    public bool IsFlowControl => false;
    public bool IsNonExtractable => true;
    public BlueprintNodeType? LegacyNodeType => BlueprintNodeType.Print;
    public FormattedStatementKind StatementKind => FormattedStatementKind.Print;
    public double NodeWidth => 100;
    public double NodeHeight => 50;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 20),
        new("Value", PinType.Any, 35)
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [
        new("Exec", PinType.Execution, 25)
    ];

    public string? GetExecutionMethodBody() => null; // 已在 BlockScriptExecutionGlobals 中手工实现

    public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

    public List<FormattedStatement> FormatInvocation(
        InvocationExpressionSyntax invoke, string blockName,
        PipelineContext context, string? assignedVar)
    {
        var args = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();
        return [new FormattedStatement
        {
            BlockName = blockName,
            Kind = FormattedStatementKind.Print,
            FunctionName = FunctionName,
            Arguments = args,
            OriginalExpression = invoke.ToString(),
            SourceLine = 0,
        }];
    }

    public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt) => node;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        var value = helper.GetInputValue(node, "Value");
        return new ExpressionStatement
        {
            Expression = $"{FunctionName}({value})",
            SourceCode = $"{FunctionName}({value});",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}
