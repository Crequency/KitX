using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// Loop 内置函数 — 循环控制流。
    /// BlockScript 语法：Loop(condition, "loopBodyBlock", "loopEndBlock")
    /// </summary>
    public class LoopFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Loop";
        public string DisplayName => "Loop";
        public bool IsFlowControl => true;
        public bool IsNonExtractable => false;
        public bool IsBlockTerminator => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Loop;
        public double NodeWidth => 120;
        public double NodeHeight => 80;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 30),
            new("Condition", PinType.Boolean, 50)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("LoopBody", PinType.Execution, 30),
            new("LoopEnd", PinType.Execution, 50)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText)
        {
            var args = invoke.ArgumentList.Arguments;
            var stmt = new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = exprText ?? invoke.ToFullString(),
                ControlType = FlowControlType.Loop
            };
            if (args.Count >= 1) stmt.ConditionExpression = args[0].Expression.ToString();
            if (args.Count >= 2) stmt.TrueBlockName = ExprUtils.GetStringLiteralValue(args[1].Expression) ?? string.Empty;
            if (args.Count >= 3) stmt.FalseBlockName = ExprUtils.GetStringLiteralValue(args[2].Expression) ?? string.Empty;
            return stmt;
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, PipelineContext context)
        {
            var arms = new List<(string PinName, string TargetBlockName)>();
            if (!string.IsNullOrEmpty(stmt.TrueBlockName))
                arms.Add(("LoopBody", stmt.TrueBlockName));
            if (!string.IsNullOrEmpty(stmt.FalseBlockName))
                arms.Add(("LoopEnd", stmt.FalseBlockName));

            if (arms.Count > 0)
            {
                context.DeferredEdges.Add(new DeferredControlFlowEdge
                {
                    SourceStatementId = stmt.StatementId,
                    Arms = arms,
                });
            }

            context.LoopNodesByParent[stmt.BlockName] = node;
        }

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var condExpr = !string.IsNullOrEmpty(stmt.ConditionPubVar)
                ? ctx.ResolveArgument(stmt.ConditionPubVar)
                : ctx.Parse(stmt.ConditionExpression ?? "false");
            return ctx.EmitNextBlockAssignment("Loop", condExpr,
                ctx.Literal(stmt.TrueBlockName ?? ""), ctx.Literal(stmt.FalseBlockName ?? ""));
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var condition = helper.GetInputValue(node, "Condition");
            return new FlowControlStatement
            {
                ControlType = FlowControlType.Loop,
                ConditionExpression = condition,
                SourceCode = $"Loop({condition}, \"\", \"\");",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() =>
        [
            new() { PinName = "LoopBody", IsLoopback = false },
            new() { PinName = "LoopEnd", IsLoopback = false }
        ];
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// Loop while condition is true (three-argument syntax).
        /// </summary>
        public string? Loop(bool condition, string trueBlock, string falseBlock)
        {
            return AdvanceTo(condition ? trueBlock : falseBlock);
        }
    }
}
