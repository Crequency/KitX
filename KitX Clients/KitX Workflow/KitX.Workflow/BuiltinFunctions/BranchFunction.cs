using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// Branch 内置函数 — 条件分支控制流。
    /// </summary>
    public class BranchFunction : IFlowControlFunctionDefinition
    {
        public string FunctionName => "Branch";
        public string DisplayName => "Branch";
        public bool IsNonExtractable => false;
        public FlowControlType FlowControlShape => FlowControlType.ConditionalJump;
        public FlowControlArgLayout ArgLayout => new(1, 2, false);
        public IReadOnlyList<string> ArmPinNames => ["True", "False"];

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 30),
            new("Condition", PinType.Boolean, 50)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("True", PinType.Execution, 30),
            new("False", PinType.Execution, 50)
        ];

        // ArgLayout dispatch obsoletes hand-written ExtractStatement.
        public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) => null;

        public string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                                   IReadOnlyList<string> flowArguments)
            => $"Branch({condition ?? ""}, \"{arms.ElementAtOrDefault(0)?.TargetBlockName ?? ""}\", \"{arms.ElementAtOrDefault(1)?.TargetBlockName ?? ""}\");";

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, ForwardConversionState context)
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
            var condExpr = ctx.Parse(stmt.ConditionExpression ?? "false");
            return ctx.EmitNextBlockAssignment("Branch", condExpr,
                ctx.Literal(stmt.TrueBlockName ?? ""), ctx.Literal(stmt.FalseBlockName ?? ""));
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var condition = helper.GetInputValue(node, "Condition");
            return new FlowControlStatement
            {
                ControlType = FlowControlType.ConditionalJump,
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
            => AdvanceTo(condition ? trueBlock : falseBlock);
    }
}
