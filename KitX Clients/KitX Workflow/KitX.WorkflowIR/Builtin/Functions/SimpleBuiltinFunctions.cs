namespace KitX.WorkflowIR.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Builtin;
using KitX.WorkflowIR.Ir;
using KitX.WorkflowIR.Ir.Ast;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// A-level builtins: logic reused verbatim from the legacy library, signatures
// adapted to the new IR types (IrPipelineStatement / IrControlFlowStatement).
//
// Pure value/side-effect builtins (Print, Pause, ReadTextFile, WriteTextFile) read
// their flat arguments off the pipeline's terminal segment and emit a single
// G.<Name>(...) call. ReadTextFile is Pure (value-producing, nestable); Print/Pause/
// WriteTextFile are SideEffect (no consumed return).
//
// Control-flow terminators (Branch, Goto, Exit) implement IParserHandler (they parse
// their own arm layout) + ICodeGenHandler (they emit G.NextBlock = G.<Op>(...); break;
// or return;). None of them produce a data output (v5.0 §7 control-flow rule).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Print builtin — output a value to the console. Side-effect, no consumed return.
/// BlockScript: <c>Print(value)</c>.
/// </summary>
public sealed class PrintFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "Print";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Value", PinType.Any, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 25),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.FlatArguments(stmt);
        if (args.Count == 0) yield break;
        yield return ctx.GInvokeStatement("Print", ctx.ResolveArgument(args[0]));
    }
}

/// <summary>
/// Pause builtin — pause execution for N milliseconds. Side-effect.
/// BlockScript: <c>Pause(milliseconds)</c>.
/// </summary>
public sealed class PauseFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "Pause";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Milliseconds", PinType.Integer, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 25),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.FlatArguments(stmt);
        if (args.Count == 0) yield break;
        yield return ctx.GInvokeStatement("Pause", ctx.ResolveArgument(args[0]));
    }
}

/// <summary>
/// ReadTextFile builtin — read a text file into a string. Pure (value-producing,
/// nestable as an expression).
/// BlockScript: <c>ReadTextFile(path)</c>.
/// </summary>
public sealed class ReadTextFileFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "ReadTextFile";
    public FunctionKind Kind => FunctionKind.Pure;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Path", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Return", PinType.String, 40),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        var assignedVar = BuiltinEmitHelpers.AssignedVariable(stmt);
        var call = ctx.GInvoke("ReadTextFile", args);
        foreach (var s in ctx.EmitValueAssignment(assignedVar, call, "string"))
            yield return s;
    }
}

/// <summary>
/// WriteTextFile builtin — write content to a text file. Side-effect.
/// BlockScript: <c>WriteTextFile(path, content)</c>.
/// </summary>
public sealed class WriteTextFileFunction : IBuiltinFunction, ICodeGenHandler
{
    public string Name => "WriteTextFile";
    public FunctionKind Kind => FunctionKind.SideEffect;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Path", PinType.String, 35),
        new("Content", PinType.String, 35),
    ];

    public IReadOnlyList<PortSpec> OutputPorts =>
    [
        new("Exec", PinType.Execution, 20),
    ];

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var args = BuiltinEmitHelpers.ResolveArguments(stmt, ctx);
        yield return ctx.GInvokeStatement("WriteTextFile", args);
    }
}

// ── Control-flow terminators (Branch / Goto / Exit) ────────────────────────────

/// <summary>
/// Branch builtin — conditional two-way control flow.
/// BlockScript: <c>Branch(cond, "trueBlock", "falseBlock")</c>.
/// </summary>
public sealed class BranchFunction : IBuiltinFunction, IParserHandler, ICodeGenHandler
{
    public string Name => "Branch";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 30),
        new("Condition", PinType.Boolean, 50),
    ];

    // v5.0 §7: control-flow terminators carry no data output pins. Their two Exec
    // arms (True/False) are structural control edges declared by the op, not data
    // ports — they are rendered by the BP layer from the op's arm layout, not read
    // from OutputPorts. OutputPorts is therefore empty (matches the registry's
    // ControlFlow invariant).
    public IReadOnlyList<PortSpec> OutputPorts => [];

    public FlowControlStatement ParseInvocation(BSCall call, int sourceLine)
    {
        var args = call.Args;
        var cond = args.ElementAtOrDefault(0)?.SourceText;
        return new FlowControlStatement
        {
            LineNumber = sourceLine,
            SourceCode = call.SourceText,
            FunctionName = "Branch",
            ConditionExpression = cond ?? "",
            FlowArguments = cond is not null ? [cond] : [],
            Arms =
            [
                new() { PinName = "True",  TargetBlockName = args.ElementAtOrDefault(1)?.AsStringLiteral() ?? "" },
                new() { PinName = "False", TargetBlockName = args.ElementAtOrDefault(2)?.AsStringLiteral() ?? "" },
            ],
        };
    }

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var cf = (IrControlFlowStatement)stmt;
        var condExpr = ctx.Parse(cf.Arguments.Length > 0 ? cf.Arguments[0] : "false");
        var trueBlock = cf.Targets.Length > 0 ? cf.Targets[0].TargetBlockName : "";
        var falseBlock = cf.Targets.Length > 1 ? cf.Targets[1].TargetBlockName : "";
        foreach (var s in ctx.EmitNextBlockAssignment("Branch", condExpr,
            ctx.Literal(trueBlock), ctx.Literal(falseBlock)))
            yield return s;
    }
}

/// <summary>
/// Goto builtin — unconditional transfer. Replaces the v4.0 <c>NextBlock = "block"</c>.
/// BlockScript: <c>Goto("blockName")</c>.
/// </summary>
public sealed class GotoFunction : IBuiltinFunction, IParserHandler, ICodeGenHandler
{
    public string Name => "Goto";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts => [new("Exec", PinType.Execution, 30)];
    // Single Exec arm ("Exec") is a structural control edge, not a data pin — OutputPorts empty.
    public IReadOnlyList<PortSpec> OutputPorts => [];

    public FlowControlStatement ParseInvocation(BSCall call, int sourceLine)
    {
        var target = call.Args.ElementAtOrDefault(0)?.AsStringLiteral() ?? "";
        return new FlowControlStatement
        {
            LineNumber = sourceLine,
            SourceCode = call.SourceText,
            FunctionName = "Goto",
            FlowArguments = target is { Length: > 0 } ? [target] : [],
            Arms = [new() { PinName = "Exec", TargetBlockName = target }],
        };
    }

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var cf = (IrControlFlowStatement)stmt;
        var target = cf.Targets.Length > 0 ? cf.Targets[0].TargetBlockName : "";
        foreach (var s in ctx.EmitNextBlockAssignment("Goto", ctx.Literal(target)))
            yield return s;
    }
}

/// <summary>
/// Exit builtin — terminate the entire script activation (runtime return).
/// Renamed from Break in v5.0: semantic is "exit this script run", not "exit loop".
/// BlockScript: <c>Exit()</c>.
/// </summary>
public sealed class ExitFunction : IBuiltinFunction, IParserHandler, ICodeGenHandler
{
    public string Name => "Exit";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts => [new("Exec", PinType.Execution, 20)];
    public IReadOnlyList<PortSpec> OutputPorts => [];

    public FlowControlStatement ParseInvocation(BSCall call, int sourceLine) => new()
    {
        LineNumber = sourceLine,
        SourceCode = call.SourceText,
        FunctionName = "Exit",
    };

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        yield return ctx.Return();
    }
}
