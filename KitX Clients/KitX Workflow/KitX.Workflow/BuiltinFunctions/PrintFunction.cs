using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;
using KitX.Workflow.Services;

namespace KitX.Workflow.BuiltinFunctions
{
    /// <summary>
    /// Print 内置函数 �?输出值到控制台�?    /// </summary>
    public class PrintFunction : IBuiltinFunctionDefinition
    {
        public string FunctionName => "Print";
        public string DisplayName => "Print";
        public bool IsNonExtractable => true;

        public IReadOnlyList<PinDescriptor> InputPins => [
            new("Exec", PinType.Execution, 20),
            new("Value", PinType.Any, 35)
        ];

        public IReadOnlyList<PinDescriptor> OutputPins => [
            new("Exec", PinType.Execution, 25)
        ];
    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
    {
        if (stmt.Arguments.Count == 0) return new();
        return new() { ctx.GInvokeStatement(FunctionName, ctx.ResolveArgument(stmt.Arguments[0])) };
    }
    }
}

namespace KitX.Workflow.BlockScripting
{
    public partial class BlockScriptExecutionGlobals
    {
        /// <summary>
        /// Print a value.
        /// </summary>
        public void Print(object? value)
        {
            var str = value?.ToString() ?? "null";
            _output.Add(str);
            WorkflowOutput.WriteLine(value);
        }
    }
}
