using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// Branch 内置函数 �?条件分支控制流�?    /// </summary>
    public class BranchFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Branch";
        public string DisplayName => "Branch";
        public bool IsNonExtractable => false;
    public bool IsBlockTerminator => true;
    public bool IsFlowControl => true;
        public FlowControlArgLayout ArgLayout => new(1, 2, false);

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 30),
            new("Condition", PinType.Boolean, 50)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("True", PinType.Execution, 30),
            new("False", PinType.Execution, 50)
        ];
        FlowControlStatement? IBuiltinFunctionDefinition.ParseInvocation(BSCall invoke, int lineNumber)
        {
            var args = invoke.Args;
            var cond = args.ElementAtOrDefault(0)?.SourceText;
            return new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = invoke.SourceText,
                FunctionName = "Branch",
                ConditionExpression = cond,
                FlowArguments = cond is not null ? [cond] : [],
                Arms =
                [
                    new() { PinName = "True",  TargetBlockName = args.ElementAtOrDefault(1)?.AsStringLiteral() ?? "" },
                    new() { PinName = "False", TargetBlockName = args.ElementAtOrDefault(2)?.AsStringLiteral() ?? "" }
                ]
            };
        }

        public string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                                   IReadOnlyList<string> flowArguments)
            => $"Branch({condition ?? ""}, \"{arms.ElementAtOrDefault(0)?.TargetBlockName ?? ""}\", \"{arms.ElementAtOrDefault(1)?.TargetBlockName ?? ""}\");";
        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            var condExpr = ctx.Parse(stmt.ConditionExpression ?? "false");
            return ctx.EmitNextBlockAssignment("Branch", condExpr,
                ctx.Literal(stmt.TrueBlockName ?? ""), ctx.Literal(stmt.FalseBlockName ?? ""));
        }
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
