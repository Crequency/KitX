using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// Goto 内置函数 (v5.0) — 无条件跳转。替代 v4.0 的 <c>NextBlock = "block"</c> 与
    /// <c>ToLoopCond</c>。作为块的终止语句，直接设置下一块并结束当前块。
    /// 见 BlockScriptGrammarRule §7.6。
    /// </summary>
    public class GotoFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Goto";
        public string DisplayName => "Goto";
        public bool IsFlowControl => true;
        public bool IsNonExtractable => false;
        public bool IsBlockTerminator => true;
        public FlowControlType? FlowControlShape => FlowControlType.UnconditionalJump;
        public double NodeWidth => 100;
        public double NodeHeight => 60;

        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 30)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 30)];

        public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText)
        {
            var args = invoke.Args;
            var stmt = new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = exprText ?? invoke.SourceText,
                ControlType = FlowControlType.UnconditionalJump
            };
            // Goto("targetBlock") — single string-literal arm.
            if (args.Count >= 1) stmt.LoopbackTarget = args[0].AsStringLiteral();
            return stmt;
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, PipelineContext context)
        {
            var target = stmt.LoopbackTarget;
            if (!string.IsNullOrEmpty(target))
            {
                context.DeferredEdges.Add(new DeferredControlFlowEdge
                {
                    SourceStatementId = stmt.StatementId,
                    Arms = [("Exec", target)],
                });
            }
        }

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            // Goto sets NextBlock directly and breaks the switch case. The runtime AdvanceTo
            // path is shared with Branch/ForLoop.
            return ctx.EmitNextBlockAssignment("Goto", ctx.Literal(stmt.LoopbackTarget ?? ""));
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper) => null;

        public IEnumerable<OutputArmDescriptor> GetOutputArms() =>
            [new() { PinName = "Exec", IsLoopback = false }];
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// Unconditional jump (v5.0 Goto). Sets NextBlock to the target and returns it.
        /// </summary>
        public string? Goto(string targetBlock) => AdvanceTo(targetBlock);
    }
}
