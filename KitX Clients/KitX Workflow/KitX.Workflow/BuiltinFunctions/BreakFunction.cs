using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions;

/// <summary>
/// Break 内置函数 — 退出当前循环。
/// </summary>
public class BreakFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "Break";
    public string DisplayName => "Break";
        public bool IsBlockTerminator => true;
        public bool IsNonExtractable => true;
        public FlowControlType? FlowControlShape => FlowControlType.LoopExit;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 20)
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [];


    public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) => null;

    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        => new() { ctx.Return() };

    public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        return new FlowControlStatement
        {
            ControlType = FlowControlType.LoopExit,
            SourceCode = "Break();",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}
