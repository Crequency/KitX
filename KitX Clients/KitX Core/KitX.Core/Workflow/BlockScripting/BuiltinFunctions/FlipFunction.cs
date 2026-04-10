using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.Pipeline;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions;

/// <summary>
/// Flip 内置函数 — 交替路由控制流。每次执行时交替选择两个输出分支之一。
/// 第一次执行走 A（奇数次），第二次走 B（偶数次），循环往复。
/// 运行级状态：从 Entry 节点重新开始运行时重置计数器。
///
/// BlockScript 语法：Flip("blockA", "blockB")
/// </summary>
public class FlipFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "Flip";
    public string DisplayName => "Flip";
    public bool IsFlowControl => true;
    public bool IsNonExtractable => true;
    public bool IsBlockTerminator => true;
    public double NodeWidth => 120;
    public double NodeHeight => 80;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 30)
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [
        new("A", PinType.Execution, 30),
        new("B", PinType.Execution, 50)
    ];

    public string? GetExecutionMethodBody() => """
        private int _flipCounter = 0;

        public void ResetRunState() => _flipCounter = 0;

        public string? Flip(string outputA, string outputB)
        {
            _flipCounter++;
            NextBlock = (_flipCounter % 2 == 1) ? outputA : outputB;
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
            ControlType = FlowControlType.Branch // Reuse Branch type for cross-block routing
        };
        if (args.Count >= 1) stmt.TrueBlockName = GetStringLiteral(args[0].Expression);
        if (args.Count >= 2) stmt.FalseBlockName = GetStringLiteral(args[1].Expression);
        return stmt;
    }

    public List<FormattedStatement> FormatInvocation(
        InvocationExpressionSyntax invoke, string blockName,
        PipelineContext context, string? assignedVar)
    {
        var args = invoke.ArgumentList.Arguments;
        var trueBlock = args.Count >= 1 ? GetStringLiteral(args[0].Expression) : "";
        var falseBlock = args.Count >= 2 ? GetStringLiteral(args[1].Expression) : "";

        return [new FormattedStatement
        {
            BlockName = blockName,
            Kind = FormattedStatementKind.Branch, // Reuse Branch kind for pipeline routing
            FunctionName = FunctionName,
            TrueBlockName = trueBlock,
            FalseBlockName = falseBlock,
            OriginalExpression = invoke.ToString(),
            SourceLine = 0,
        }];
    }

    public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt) => node;

    public void OnNodeCreated(BlueprintNode node, FormattedStatement stmt, PipelineContext context)
    {
        var arms = new List<(string PinName, string TargetBlockName)>();
        if (!string.IsNullOrEmpty(stmt.TrueBlockName))
            arms.Add(("A", stmt.TrueBlockName));
        if (!string.IsNullOrEmpty(stmt.FalseBlockName))
            arms.Add(("B", stmt.FalseBlockName));
        context.DeferredEdges.Add(new DeferredControlFlowEdge
        {
            SourceStatementId = stmt.StatementId,
            Arms = arms,
        });
    }

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        return new ExpressionStatement
        {
            Expression = "Flip(\"\", \"\")",
            SourceCode = "Flip(\"\", \"\");",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() =>
    [
        new() { PinName = "A", IsLoopback = false },
        new() { PinName = "B", IsLoopback = false }
    ];

    private static string GetStringLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit ? lit.Token.ValueText : expr.ToString().Trim('"');
}
