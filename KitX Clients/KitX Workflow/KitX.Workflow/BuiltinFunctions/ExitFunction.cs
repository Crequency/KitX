using Microsoft.CodeAnalysis.CSharp.Syntax;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;

namespace KitX.Workflow.BuiltinFunctions;

/// <summary>
/// Exit 内置函数 — 结束整个工作流的本次激活（运行时 return，§7.4）。
/// v5.0 改名自 Break：语义是"退出脚本本次激活"而非"跳出循环"。
/// 跳出循环通过 Branch+Goto 组合表达。
/// </summary>
public class ExitFunction : IBuiltinFunctionDefinition
{
    public string FunctionName => "Exit";
    public string DisplayName => "Exit";
    public bool IsNonExtractable => true;
    public bool IsBlockTerminator => true;
    public bool IsFlowControl => true;
    public FlowControlArgLayout? ArgLayout => new(0, 0, false);

    public IReadOnlyList<PinDescriptor> InputPins => [new("Exec", PinType.Execution, 20)];
    public IReadOnlyList<PinDescriptor> OutputPins => [];

    FlowControlStatement? IBuiltinFunctionDefinition.ParseInvocation(BSCall invoke, int lineNumber)
        => new()
        {
            LineNumber = lineNumber,
            SourceCode = invoke.SourceText,
            FunctionName = "Exit",
        };

    public string RenderSource(string? condition, IReadOnlyList<BranchArm> arms,
                               IReadOnlyList<string> flowArguments)
        => "Exit();";

    public List<StatementSyntax> EmitStatements(CFGStatement stmt, CSEmitContext ctx)
        => new() { ctx.Return() };
}
