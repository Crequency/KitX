using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Conversion;
using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.BuiltinFunctions
{
    /// <summary>
    /// Pause 内置函数 — 暂停执行指定毫秒数。
    /// </summary>
    public class PauseFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Pause";
        public string DisplayName => "Pause";
        public bool IsFlowControl => false;
        public bool IsNonExtractable => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Pause;
        public double NodeWidth => 100;
        public double NodeHeight => 50;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Milliseconds", PinType.Integer, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 25)
        ];

        public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
    {
        if (stmt.Arguments.Count == 0) return new();
        return new() { ctx.GInvokeStatement(FunctionName, ctx.ResolveArgument(stmt.Arguments[0])) };
    }

        public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

        public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        {
            var ms = helper.GetInputValue(node, "Milliseconds");
            return new ExpressionStatement
            {
                Expression = $"{FunctionName}({ms})",
                SourceCode = $"{FunctionName}({ms});",
                LineNumber = 1
            };
        }

        public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
    }
}

namespace KitX.Core.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// Pause execution.
        /// </summary>
        public void Pause(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }
    }
}
