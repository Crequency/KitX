using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions;

/// <summary>
/// Exit 内置函数 — 结束整个工作流的本次激活（运行时 return，§7.4）。
/// v5.0 改名自 Break：语义是"退出脚本激活"而非"跳出循环"。
/// 跳出循环通过 Branch+Goto 组合表达。
/// </summary>
public class ExitFunction : IFlowControlFunctionDefinition
{
    public string FunctionName => "Exit";
    public string DisplayName => "Exit";
    public bool IsNonExtractable => true;
    public FlowControlType FlowControlShape => FlowControlType.ScriptReturn;
    public FlowControlArgLayout ArgLayout => new(0, 0, false);
    public IReadOnlyList<string> ArmPinNames => [];

    public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20)];
    public IReadOnlyList<PinDescriptor> OutputPins => [];

    public BlockStatement? ExtractStatement(BSCall invoke, int lineNumber, string? exprText) =>
        new FlowControlStatement
        {
            LineNumber = lineNumber,
            SourceCode = exprText ?? "Exit();",
            ControlType = FlowControlType.ScriptReturn
        };

    public string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                               IReadOnlyList<string> flowArguments)
        => "Exit();";

    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        => new() { ctx.Return() };

    public BlueprintNode ConfigureNode(BlueprintNode node, CFGStatement stmt) => node;

    public void OnNodeCreated(BlueprintNode node, CFGStatement stmt, ForwardConversionState context) { }

    public BlockStatement? ToStatement(BlueprintNode node, INodeExportHelper helper)
        => new FlowControlStatement { ControlType = FlowControlType.ScriptReturn, SourceCode = "Exit();", LineNumber = 1 };

    public IEnumerable<OutputArmDescriptor> GetOutputArms() => [];
}
