using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    using static KitX.Workflow.BlockScripting.BlockScriptWellKnown;
    /// <summary>
    /// Switch 内置函数 �?整数索引 N 路分支控制流�?    /// BlockScript 语法：NextBlock = Switch(selector, "defaultBlock", "b0", "b1", ...)
    /// <para>
    /// 语义：selector 为整数。命�?<c>0 �?selector &lt; N</c>)时走�?selector 个分支块;
    /// 越界�?default �?arg[0])�?    /// </para>
    /// <para>
    /// Arms 布局�?c>[Default, 0, 1, ..., N-1]</c> —�?第一个是 default,其余按索引顺序�?    /// 蓝图节点初始输出端口�?[Default, 0];编辑器在 0 被连接后自动追加 1�?�?..(
    /// �?<see cref="OutputVariadic"/>)�?    /// </para>
    /// </summary>
    public class SwitchFunction : IBuiltinFunctionDefinition
    {
        private const double PinGap = 20.0;

        public string FunctionName => "Switch";
        public string DisplayName => "Switch";
        public bool IsNonExtractable => false;
    public bool IsBlockTerminator => true;
    public bool IsFlowControl => true;
        public FlowControlArgLayout ArgLayout => new(1, 1, true);

        public IReadOnlyList<PinDescriptor> InputPins => [
            new(Pins.Exec, PinType.Execution, 20),
            new(Pins.Selector, PinType.Integer, 40)
        ];

        /// <summary>固定初始模板：[Default, 0]。编辑器�?<see cref="OutputVariadic"/> 动态追加�?/summary>
        public IReadOnlyList<PinDescriptor> OutputPins => [
            new(Pins.Default, PinType.Execution, 20),
            new("0", PinType.Execution, 40)
        ];
        /// <summary>输出侧变长：0 被连接后追加 "1"�?2"�?..(Execution 类型)�?/summary>
        public VariadicPinSpec? OutputVariadic => new(string.Empty, 1, PinType.Execution);
        FlowControlStatement? IBuiltinFunctionDefinition.ParseInvocation(BSCall invoke, int lineNumber)
        {
            var args = invoke.Args;
            var selector = args.ElementAtOrDefault(0)?.SourceText;
            var stmt = new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = invoke.SourceText,
                FunctionName = "Switch",
                ConditionExpression = selector,
                FlowArguments = selector is not null ? [selector] : [],
            };
            // Arms: first arg after selector is default, then 0, 1, ..., N-1
            for (int i = 1; i < args.Count; i++)
            {
                var blockName = args[i]?.AsStringLiteral() ?? "";
                var pinName = i == 1 ? "Default" : (i - 2).ToString();
                stmt.Arms.Add(new BranchArm { PinName = pinName, TargetBlockName = blockName });
            }
            if (stmt.Arms.Count == 0)
                stmt.Arms.Add(new BranchArm { PinName = "Default", TargetBlockName = "" });
            return stmt;
        }

        public string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                                   IReadOnlyList<string> flowArguments)
        {
            if (arms.Count == 0) return $"Switch({condition ?? ""}, \"\");";
            var defaultBlock = arms[0].TargetBlockName;
            var blocks = arms.Skip(1).Select(a => $"\"{a.TargetBlockName}\"");
            return $"Switch({condition ?? ""}, \"{defaultBlock}\", {string.Join(", ", blocks)});";
        }

        /// <summary>
        /// BS→BP 导入：按 arm 数动态生成输�?Pin 描述�?[Default, 0, 1, ..., N-1]�?        /// 覆写默认实现(返回固定 OutputPins)以匹配变�?arm�?        /// </summary>
        public IReadOnlyList<PinDescriptor> GetOutputPinsFor(CFGStatement stmt)
        {
            var pins = new List<PinDescriptor>();
            for (int i = 0; i < stmt.Arms.Count; i++)
            {
                var arm = stmt.Arms[i];
                pins.Add(new PinDescriptor(arm.PinName, PinType.Execution, 20 + i * PinGap));
            }
            return pins;
        }

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            // G.NextBlock = G.Switch(selector, "default", "b0", "b1", ...); break;
            var selExpr = ctx.Parse(stmt.ConditionExpression ?? "0");

            // EmitNextBlockAssignment(member, params args) �?prepend selector then all arm blocks.
            var args = new List<ExpressionSyntax> { selExpr };
            args.AddRange(stmt.Arms.Select(a => (ExpressionSyntax)ctx.Literal(a.TargetBlockName ?? "")));
            return ctx.EmitNextBlockAssignment("Switch", args.ToArray());
        }

        /// <summary>
        /// Traces an output execution pin's connection to find the target block name.
        /// </summary>
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// Switch �?N-way dispatch by integer index. selector out of range �?defaultBlock.
        /// </summary>
        public string? Switch(int selector, string defaultBlock, params string[] blocks)
        {
            var target = (selector < 0 || selector >= blocks.Length) ? defaultBlock : blocks[selector];
            return AdvanceTo(target);
        }
    }
}
