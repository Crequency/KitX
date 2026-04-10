using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.Pipeline;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions;

/// <summary>
/// LoopBodyEnd 内置函数 — 标记循环体的结尾并返回循环条件块。
/// BlockScript 语法：LoopBodyEnd("parentBlockName")
/// </summary>
public class LoopBodyEndFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "LoopBodyEnd";
    public string DisplayName => "LoopBodyEnd";
    public bool IsFlowControl => true;
    public bool IsNonExtractable => false;
    public bool IsBlockTerminator => true;
    public double NodeWidth => 80;
    public double NodeHeight => 60;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 30)
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [];

    public string? GetExecutionMethodBody() => """
        public string? LoopBodyEnd(string parentBlockName)
        {
            NextBlock = $"{parentBlockName}_Loop";
            return NextBlock;
        }
        """;

    public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText)
    {
        var args = invoke.ArgumentList.Arguments;
        var stmt = new FlowControlStatement
        {
            LineNumber = lineNumber,
            SourceCode = exprText ?? invoke.ToFullString(),
            ControlType = FlowControlType.LoopBodyEnd
        };
        if (args.Count >= 1) stmt.LoopBodyEndReturnTo = GetStringLiteral(args[0].Expression);
        return stmt;
    }

    public List<FormattedStatement> FormatInvocation(
        InvocationExpressionSyntax invoke, string blockName,
        PipelineContext context, string? assignedVar)
    {
        // LoopBodyEnd is handled through FormatFlowControl, not FormatInvocation.
        return [];
    }

    public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt) => node;

    public void OnNodeCreated(BlueprintNode node, FormattedStatement stmt, PipelineContext context)
    {
        // LoopBodyEnd does not create a node — handled by ProcessStatement's special case.
        // No additional OnNodeCreated logic needed.
    }

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        return new FlowControlStatement
        {
            ControlType = FlowControlType.LoopBodyEnd,
            SourceCode = "LoopBodyEnd(\"\");",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];

    private static string GetStringLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit ? lit.Token.ValueText : expr.ToString().Trim('"');
}
