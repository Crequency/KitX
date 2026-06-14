using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Conversion;
using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.BuiltinFunctions
{
    /// <summary>
    /// ToLoopCond 内置函数 — 标记循环体的结尾并返回循环条件块。
    /// BlockScript 语法：ToLoopCond("parentBlockName")
    /// </summary>
    public class ToLoopCondFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ToLoopCond";
        public string DisplayName => "ToLoopCond";
        public bool IsFlowControl => true;
        public bool IsNonExtractable => false;
        public bool IsBlockTerminator => true;
        public CFGStatementKind StatementKind => CFGStatementKind.ToLoopCond;
        public double NodeWidth => 80;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 30)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 30)
        ];

        /// <summary>
        /// 已在 partial class BlockScriptExecutionGlobals 中实现。
        /// 此处返回 null 表示执行完全通过 Globals 方法调用处理。
        /// </summary>


        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText)
        {
            var args = invoke.ArgumentList.Arguments;
            var stmt = new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = exprText ?? invoke.ToFullString(),
                ControlType = FlowControlType.ToLoopCond
            };
            if (args.Count >= 1) stmt.ToLoopCondReturnTo = GetStringLiteral(args[0].Expression);
            return stmt;
        }

        public List<CFGStatement> FormatInvocation(
            InvocationExpressionSyntax invoke, string blockName,
            PipelineContext context, string? assignedVar)
        {
            // ToLoopCond is handled through FormatFlowControl, not FormatInvocation.
            return [];
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, PipelineContext context)
        {
            var returnToBlock = stmt.ToLoopCondReturnTo;
            if (!string.IsNullOrEmpty(returnToBlock))
            {
                context.DeferredEdges.Add(new DeferredControlFlowEdge
                {
                    SourceStatementId = stmt.StatementId,
                    Arms = [("Exec", returnToBlock)],
                });
            }
        }

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
            => ctx.EmitNextBlockAssignment("ToLoopCond", ctx.Literal(stmt.ToLoopCondReturnTo ?? ""));

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            // Trace exec output connection to find the target block name
            var execOut = node.OutputPins.FirstOrDefault(p => p.Name == "Exec");
            if (execOut != null)
            {
                var conn = helper.Blueprint.Connections
                    .FirstOrDefault(c => c.SourcePinId == execOut.Id);
                if (conn != null)
                {
                    var targetNode = helper.Blueprint.GetNodeById(conn.TargetNodeId);
                    if (targetNode != null)
                    {
                        var targetScope = helper.Blueprint.BlockScopes
                            .FirstOrDefault(s => s.NodeIds.Contains(targetNode.Id));
                        if (targetScope != null)
                        {
                            var returnTo = targetScope.Name;
                            return new FlowControlStatement
                            {
                                ControlType = FlowControlType.ToLoopCond,
                                ToLoopCondReturnTo = returnTo,
                                SourceCode = $"NextBlock = ToLoopCond(\"{returnTo}\");",
                                LineNumber = 1
                            };
                        }
                    }
                }
            }

            return new FlowControlStatement
            {
                ControlType = FlowControlType.ToLoopCond,
                SourceCode = "ToLoopCond();",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [
            new() { PinName = "Exec", IsLoopback = false }
        ];

        private static string GetStringLiteral(ExpressionSyntax expr) =>
            expr is LiteralExpressionSyntax lit ? lit.Token.ValueText : expr.ToString().Trim('"');
    }
}

namespace KitX.Core.Workflow.BlockScripting
{
    // ──────────────────────────────────────────────
    // Partial class — ToLoopCond 的运行时方法
    // ──────────────────────────────────────────────
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// ToLoopCond - marks the end of a loop body and returns to the loop condition block.
        /// </summary>
        public string? ToLoopCond(string parentBlockName)
        {
            NextBlock = parentBlockName;
            return NextBlock;
        }
    }
}
