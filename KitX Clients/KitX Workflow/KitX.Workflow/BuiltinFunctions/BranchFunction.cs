using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// Branch 内置函数 — 条件分支控制流。
    /// </summary>
    public class BranchFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Branch";
        public string DisplayName => "Branch";
        public bool IsFlowControl => true;
        public bool IsNonExtractable => false;
        public bool IsBlockTerminator => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Branch;
        public double NodeWidth => 120;
        public double NodeHeight => 80;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 30),
            new("Condition", PinType.Boolean, 50)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("True", PinType.Execution, 30),
            new("False", PinType.Execution, 50)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText)
        {
            var args = invoke.ArgumentList.Arguments;
            var stmt = new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = exprText ?? invoke.ToFullString(),
                ControlType = FlowControlType.Branch
            };
            if (args.Count >= 1) stmt.ConditionExpression = args[0].Expression.ToString();
            if (args.Count >= 2) stmt.TrueBlockName = ExprUtils.GetStringLiteralValue(args[1].Expression) ?? string.Empty;
            if (args.Count >= 3) stmt.FalseBlockName = ExprUtils.GetStringLiteralValue(args[2].Expression) ?? string.Empty;
            return stmt;
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, PipelineContext context)
        {
            if (!string.IsNullOrEmpty(stmt.TrueBlockName) || !string.IsNullOrEmpty(stmt.FalseBlockName))
            {
                var arms = new List<(string PinName, string TargetBlockName)>();
                if (!string.IsNullOrEmpty(stmt.TrueBlockName))
                    arms.Add(("True", stmt.TrueBlockName));
                if (!string.IsNullOrEmpty(stmt.FalseBlockName))
                    arms.Add(("False", stmt.FalseBlockName));
                context.DeferredEdges.Add(new DeferredControlFlowEdge
                {
                    SourceStatementId = stmt.StatementId,
                    Arms = arms,
                });
            }
        }

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var condExpr = !string.IsNullOrEmpty(stmt.ConditionPubVar)
                ? ctx.ResolveArgument(stmt.ConditionPubVar)
                : ctx.Parse(stmt.ConditionExpression ?? "false");
            return ctx.EmitNextBlockAssignment("Branch", condExpr,
                ctx.Literal(stmt.TrueBlockName ?? ""), ctx.Literal(stmt.FalseBlockName ?? ""));
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var condition = helper.GetInputValue(node, "Condition");
            return new FlowControlStatement
            {
                ControlType = FlowControlType.Branch,
                ConditionExpression = condition,
                SourceCode = $"Branch({condition}, \"\", \"\");",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() =>
        [
            new() { PinName = "True", IsLoopback = false },
            new() { PinName = "False", IsLoopback = false }
        ];
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// Condition branch - sets NextBlock and returns the target block name.
        /// </summary>
        public string? Branch(bool condition, string trueBlock, string falseBlock)
        {
            return AdvanceTo(condition ? trueBlock : falseBlock);
        }
    }
}
