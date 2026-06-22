using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// ForLoop 内置函数 (v5.0) — 计数循环，counter 完全内化。
    /// <para>
    /// 参数：<c>ForLoop(from, to, step, indexName, bodyBlock, endBlock)</c>。
    /// counter 是节点内部状态：首次进入 <c>index = from</c>，每次 body 回跳（经 Goto）
    /// <c>index += step</c>。condition 内建（<c>from ≤ index &lt; to</c> 或反向）。
    /// index 通过 <c>indexName</c> 参数命名，注入到 LoopBody 作用域（只读，循环注入变量，§3.4/§7.1）。
    /// </para>
    /// <para>
    /// 无数据输出引脚（控制流函数通则，§7）。两臂：LoopBody（进入循环体）、LoopEnd（循环结束）。
    /// </para>
    /// </summary>
    public class ForLoopFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "ForLoop";
        public string DisplayName => "ForLoop";
        public bool IsFlowControl => true;
        public bool IsNonExtractable => false;
        public bool IsBlockTerminator => true;
        public FlowControlType? FlowControlShape => FlowControlType.IterativeCounted;
        public double NodeWidth => 140;
        public double NodeHeight => 90;

        public IReadOnlyList<PinDescriptor> InputPins =>
        [
            new("Exec", PinType.Execution, 20),
            new("From", PinType.Integer, 45),
            new("To", PinType.Integer, 70),
            new("Step", PinType.Integer, 95)
        ];

        // Only Execution output pins (§7 control-flow no-data-output rule).
        public IReadOnlyList<PinDescriptor> OutputPins =>
        [
            new("LoopBody", PinType.Execution, 30),
            new("LoopEnd", PinType.Execution, 60)
        ];

        public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText)
        {
            var args = invoke.Args;
            var stmt = new FlowControlStatement
            {
                LineNumber = lineNumber,
                SourceCode = exprText ?? invoke.SourceText,
                ControlType = FlowControlType.IterativeCounted
            };
            // ForLoop(from, to, step, indexName, bodyBlock, endBlock)
            // from/to/step are integer expressions stored in Arguments; indexName/body/end as arms.
            if (args.Count >= 1) stmt.ConditionExpression = args[0].SourceText; // reuse for "from" textual
            if (args.Count >= 4)
            {
                // Arms: indexName carried as a sentinel on the TrueBlockName metadata is awkward;
                // store indexName in Arguments[3] and body/end as arms.
                stmt.TrueBlockName = args[4].AsStringLiteral();   // bodyBlock
                if (args.Count >= 5) stmt.FalseBlockName = args[5].AsStringLiteral(); // endBlock
            }
            return stmt;
        }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, ForwardConversionState context)
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
        }

        public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        {
            // The CFG statement's Arguments carry from/to/step/indexName (strings).
            // Generated form: G.NextBlock = G.ForLoop(from, to, step, "indexName", "body", "end"); break;
            var args = stmt.Arguments;
            var from = args.Count > 0 ? ctx.ResolveArgument(args[0]) : ctx.Literal("0");
            var to = args.Count > 1 ? ctx.ResolveArgument(args[1]) : ctx.Literal("0");
            var step = args.Count > 2 ? ctx.ResolveArgument(args[2]) : ctx.Literal("1");
            var indexName = args.Count > 3 ? ctx.Literal(args[3].Trim('"')) : ctx.Literal("i");
            return ctx.EmitNextBlockAssignment("ForLoop", from, to, step, indexName,
                ctx.Literal(stmt.TrueBlockName ?? ""), ctx.Literal(stmt.FalseBlockName ?? ""));
        }

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper) => null;

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
        /// Per-loop counter state, keyed by the body block name (unique per ForLoop in a script).
        /// Resets when a fresh loop run begins (counter absent or completed).
        /// </summary>
        private readonly Dictionary<string, (int index, int to, int step, bool descending)> _forLoopCounters = new();

        /// <summary>
        /// Counted loop (v5.0 ForLoop). Maintains an internal counter keyed by the body block.
        /// On each call: if no counter yet, initialise at <paramref name="from"/>; otherwise advance
        /// by <paramref name="step"/>. Then evaluate <c>from ≤ index &lt; to</c> (or descending) and
        /// jump to <paramref name="bodyBlock"/> (writing the index into the named loop-injected
        /// variable first) or <paramref name="endBlock"/>.
        /// </summary>
        public string? ForLoop(int from, int to, int step, string indexName,
            string bodyBlock, string endBlock)
        {
            if (!_forLoopCounters.TryGetValue(bodyBlock, out var state))
            {
                state = (from, to, step, step < 0);
                _forLoopCounters[bodyBlock] = state;
            }

            var (index, _, _, descending) = state;
            bool continueLoop = descending ? index > to : index < to;

            if (!continueLoop)
            {
                _forLoopCounters.Remove(bodyBlock);
                return AdvanceTo(endBlock);
            }

            // Inject the current index into the named loop variable (read-only in body).
            // The body reads it via the normal variable store under indexName.
            Set(indexName, index);

            // Advance the counter for the next iteration (when body re-enters via Goto back here).
            _forLoopCounters[bodyBlock] = (index + step, to, step, descending);

            return AdvanceTo(bodyBlock);
        }
    }
}
