using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// Goto 内置函数 (v5.0) �?无条件跳转。替�?v4.0 �?<c>NextBlock = "block"</c> �?    /// <c>ToLoopCond</c>。作为块的终止语句，直接设置下一块并结束当前块�?    /// �?BlockScriptGrammarRule §7.6�?    /// </summary>
    public class GotoFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Goto";
        public string DisplayName => "Goto";
        public bool IsNonExtractable => false;
    public bool IsBlockTerminator => true;
    public bool IsFlowControl => true;
        public FlowControlArgLayout ArgLayout => new(0, 1, false);

        public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 30)];
        public IReadOnlyList<PinDescriptor> OutputPins => [new("Exec", PinType.Execution, 30)];
        FlowControlStatement? IBuiltinFunctionDefinition.ParseInvocation(BSCall invoke, int lineNumber)
        {
            var target = invoke.Args.ElementAtOrDefault(0)?.AsStringLiteral() ?? "";
            return new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = invoke.SourceText,
                FunctionName = "Goto",
                FlowArguments = target is { Length: > 0 } ? [target] : [],
                Arms =
                [
                    new() { PinName = "Exec", TargetBlockName = target }
                ]
            };
        }

        public string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                                   IReadOnlyList<string> flowArguments)
            => !string.IsNullOrEmpty(arms.ElementAtOrDefault(0)?.TargetBlockName)
                ? $"Goto(\"{arms[0].TargetBlockName}\");"
                : "Goto();";
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
            => ctx.EmitNextBlockAssignment("Goto", ctx.Literal(stmt.TrueBlockName ?? ""));
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
