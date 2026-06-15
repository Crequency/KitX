using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Core.Workflow.Conversion;
using KitX.Core.Workflow.CFG;
using KitX.Core.Workflow.BlockScripting;

namespace KitX.Core.Workflow.BuiltinFunctions;

/// <summary>
/// Break 内置函数 — 退出当前循环。
/// </summary>
public class BreakFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "Break";
    public string DisplayName => "Break";
    public bool IsFlowControl => true;
        public bool IsBlockTerminator => true;
        public bool IsNonExtractable => true;
        public CFGStatementKind StatementKind => CFGStatementKind.Break;
    public double NodeWidth => 100;
    public double NodeHeight => 40;

    public IReadOnlyList<PinDescriptor> InputPins => [
        new("Exec", PinType.Execution, 20)
    ];

    public IReadOnlyList<PinDescriptor> OutputPins => [];


    public BlockStatement? ExtractStatement(InvocationExpressionSyntax invoke, int lineNumber, string? exprText) => null;

    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        => new() { ctx.Return() };

    public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
    {
        return new FlowControlStatement
        {
            ControlType = FlowControlType.Break,
            SourceCode = "Break();",
            LineNumber = 1
        };
    }

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}
