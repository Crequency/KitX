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
    /// Switch 内置函数 — 整数索引 N 路分支控制流。
    /// BlockScript 语法：NextBlock = Switch(selector, "defaultBlock", "b0", "b1", ...)
    /// <para>
    /// 语义：selector 为整数。命中(<c>0 ≤ selector &lt; N</c>)时走第 selector 个分支块;
    /// 越界走 default 块(arg[0])。
    /// </para>
    /// <para>
    /// Arms 布局：<c>[Default, 0, 1, ..., N-1]</c> —— 第一个是 default,其余按索引顺序。
    /// 蓝图节点初始输出端口为 [Default, 0];编辑器在 0 被连接后自动追加 1、2、...(
    /// 见 <see cref="OutputVariadic"/>)。
    /// </para>
    /// </summary>
    public class SwitchFunction : IBuiltinFunctionDefinition
    {
        private const double PinGap = 20.0;

        public string FunctionName => "Switch";
        public string DisplayName => "Switch";
        public bool IsNonExtractable => false;
        public bool IsBlockTerminator => true;
        public FlowControlType? FlowControlShape => FlowControlType.IndexedDispatch;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new(Pins.Exec, PinType.Execution, 20),
            new(Pins.Selector, PinType.Integer, 40)
        ];

        /// <summary>固定初始模板：[Default, 0]。编辑器按 <see cref="OutputVariadic"/> 动态追加。</summary>
        public IReadOnlyList<PinDescriptor> OutputPins => [
            new(Pins.Default, PinType.Execution, 20),
            new("0", PinType.Execution, 40)
        ];
        /// <summary>输出侧变长：0 被连接后追加 "1"、"2"、...(Execution 类型)。</summary>
        public VariadicPinSpec? OutputVariadic => new(string.Empty, 1, PinType.Execution);

        /// <summary>
        /// BS 解析：从 NextBlock = Switch(selector, "default", "b0", "b1", ...) 提取。
        /// arg[0]=default,arg[1..N]=分支块。Arms = [Default, 0, 1, ..., N-1]。
        /// </summary>
        public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText)
        {
            var args = invoke.Args;
            var stmt = new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = exprText ?? invoke.SourceText,
                ControlType = FlowControlType.IndexedDispatch
            };

            if (args.Count >= 1)
                stmt.ConditionExpression = args[0].SourceText;

            // arg[1] = default block; arg[2..N] = branch blocks b0, b1, ...
            if (args.Count >= 2)
            {
                var defaultBlock = args[1].AsStringLiteral() ?? string.Empty;
                stmt.Arms.Add(new BranchArm { PinName = Pins.Default, TargetBlockName = defaultBlock });
            }
            else
            {
                stmt.Arms.Add(new BranchArm { PinName = Pins.Default, TargetBlockName = string.Empty });
            }

            for (int i = 2; i < args.Count; i++)
            {
                var block = args[i].AsStringLiteral() ?? string.Empty;
                stmt.Arms.Add(new BranchArm { PinName = (i - 2).ToString(), TargetBlockName = block });
            }

            return stmt;
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt)
        {
            // Ensure the node's output execution pins match the statement's arm count.
            // The descriptor's fixed OutputPins is [Default, 0]; append 1, 2, ... up to Arms.Count.
            var execOuts = node.OutputPins.Where(p => p.Type == PinType.Execution).ToList();
            // Keep existing pins; append missing ones for arms beyond the base set.
            for (int i = execOuts.Count; i < stmt.Arms.Count; i++)
            {
                node.OutputPins.Add(new BlueprintPin
                {
                    Name = (i - 1).ToString(),  // arms[1] → "0", arms[2] → "1", ...
                    Direction = PinDirection.Output,
                    Type = PinType.Execution
                });
            }
            return node;
        }

        /// <summary>
        /// BS→BP 导入：按 arm 数动态生成输出 Pin 描述符 [Default, 0, 1, ..., N-1]。
        /// 覆写默认实现(返回固定 OutputPins)以匹配变长 arm。
        /// </summary>
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

        public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, ForwardConversionState context)
        {
            // Build deferred edges: one arm per output pin (Default/0/1/...).
            if (stmt.Arms.Count == 0) return;

            var arms = stmt.Arms
                .Where(a => !string.IsNullOrEmpty(a.TargetBlockName))
                .Select(a => (a.PinName, a.TargetBlockName))
                .ToList();

            context.DeferredEdges.Add(new DeferredControlFlowEdge
            {
                SourceStatementId = stmt.StatementId,
                Arms = arms,
            });
        }

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            // G.NextBlock = G.Switch(selector, "default", "b0", "b1", ...); break;
            var selExpr = ctx.Parse(stmt.ConditionExpression ?? "0");

            // EmitNextBlockAssignment(member, params args) — prepend selector then all arm blocks.
            var args = new List<ExpressionSyntax> { selExpr };
            args.AddRange(stmt.Arms.Select(a => (ExpressionSyntax)ctx.Literal(a.TargetBlockName ?? "")));
            return ctx.EmitNextBlockAssignment("Switch", args.ToArray());
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var selector = helper.GetInputValue(node, Pins.Selector);
            var stmt = new FlowControlStatement
            {
                ControlType = FlowControlType.IndexedDispatch,
                ConditionExpression = selector,
                LineNumber = 1
            };

            // Collect arms from the node's output execution pins (Default, 0, 1, ...).
            foreach (var pin in node.OutputPins.Where(p => p.Type == PinType.Execution))
            {
                var target = ResolveArmTarget(node, pin, helper);
                stmt.Arms.Add(new BranchArm { PinName = pin.Name, TargetBlockName = target ?? string.Empty });
            }

            stmt.RegenerateSourceCode();
            return stmt;
        }

        /// <summary>
        /// Traces an output execution pin's connection to find the target block name.
        /// </summary>
        private static string? ResolveArmTarget(BlueprintNode node, BlueprintPin pin, INodeExportHelper helper)
        {
            var conn = helper.Blueprint.Connections.FirstOrDefault(c => c.SourcePinId == pin.Id);
            if (conn == null) return null;
            var targetNode = helper.Blueprint.GetNodeById(conn.TargetNodeId);
            if (targetNode == null) return null;
            var scope = helper.Blueprint.BlockScopes.FirstOrDefault(s => s.NodeIds.Contains(targetNode.Id));
            return scope?.Name;
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];  // variadic; arms come from the node's actual pins
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// Switch — N-way dispatch by integer index. selector out of range → defaultBlock.
        /// </summary>
        public string? Switch(int selector, string defaultBlock, params string[] blocks)
        {
            var target = (selector < 0 || selector >= blocks.Length) ? defaultBlock : blocks[selector];
            return AdvanceTo(target);
        }
    }
}
