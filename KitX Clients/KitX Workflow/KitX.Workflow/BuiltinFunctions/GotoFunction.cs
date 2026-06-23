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
        public bool IsNonExtractable => false;
    public bool IsBlockTerminator => true;
    public bool IsFlowControl => true;
        public FlowControlArgLayout ArgLayout => new(0, 1, false);
        FlowControlArgLayout? IBuiltinFunctionDefinition.ArgLayout => new(0, 1, false);
        public IReadOnlyList<string> ArmPinNames => ["Exec"];

        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 30)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 30)];

        // ArgLayout dispatch obsoletes hand-written ExtractStatement.
        public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) => null;

        public string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                                   IReadOnlyList<string> flowArguments)
            => !string.IsNullOrEmpty(arms.ElementAtOrDefault(0)?.TargetBlockName)
                ? $"Goto(\"{arms[0].TargetBlockName}\");"
                : "Goto();";

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, ForwardConversionState context)
        {
            var target = stmt.TrueBlockName;
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
            => ctx.EmitNextBlockAssignment("Goto", ctx.Literal(stmt.TrueBlockName ?? ""));

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
