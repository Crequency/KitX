using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Workflow.Blueprint.CFG;
using KitX.Core.Workflow.Blueprint.Pipeline;
using System.Threading;

namespace KitX.Core.Workflow.BlockScripting.BuiltinFunctions
{
    /// <summary>
    /// Pause 内置函数 — 暂停执行指定毫秒数。
    /// </summary>
    public class PauseFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Pause";
        public string DisplayName => "Pause";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public BlueprintNodeType? LegacyNodeType => BlueprintNodeType.Pause;
        public CFGStatementKind StatementKind => CFGStatementKind.Pause;
        public double NodeWidth => 100;
        public double NodeHeight => 50;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Milliseconds", PinType.Integer, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 25)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

        public List<FormattedStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            var args = invoke.ArgumentList.Arguments.Select(a => a.Expression.ToString()).ToList();
            return [new FormattedStatement
            {
                BlockName = blockName,
                Kind = CFGStatementKind.Pause,
                FunctionName = FunctionName,
                Arguments = args,
                OriginalExpression = invoke.ToString(),
                SourceLine = 0,
            }];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, FormattedStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var ms = helper.GetInputValue(node, "Milliseconds");
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}({ms})",
                SourceCode = $"{FunctionName}({ms});",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}

namespace KitX.Core.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// Pause execution.
        /// </summary>
        public void Pause(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }
    }
}
