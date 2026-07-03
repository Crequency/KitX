namespace KitX.WorkflowIR.Builtin.Functions;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Builtin;
using KitX.WorkflowIR.Ir;
using KitX.WorkflowIR.Ir.Ast;
using KitX.WorkflowIR.Ir.Lowering;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// ─────────────────────────────────────────────────────────────────────────────
// B-level control-flow builtins: ForLoop and Switch.
//
// ForLoop: a counted loop (from/to/step/indexName) whose runtime counter state is
// deferred to Phase 7. Its descriptor declares the indexName as an injected
// variable: the lowering handler registers it via the LoweringContext allocator so
// downstream name-resolution sees it, and the codegen handler emits references to
// it through G.Get("indexName"). It is the only function implementing
// IBpReverseHandler — its BP canvas alias "Loop" replaces the legacy
// BpEditApplier.DashboardToCfgName hardcoded entry.
//
// Switch: N-way integer dispatch with a variadic output (Default + 0..N-1 arms).
// The legacy GetOutputPinsFor orphan is dropped — the variadic spec now drives pin
// growth directly.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ForLoop builtin — a counted loop with counter fully internalised.
/// BlockScript: <c>ForLoop(from, to, step, "indexName", "bodyBlock", "endBlock")</c>.
/// The counter is node-internal state (Phase 7 runtime); the index value is injected
/// into the body scope under <c>indexName</c> (read-only loop-injected variable, §3.4/§7.1).
/// </summary>
public sealed class ForLoopFunction : IBuiltinFunction, IParserHandler, ILoweringHandler,
    ICodeGenHandler, IBpReverseHandler
{
    public string Name => "ForLoop";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("From", PinType.Integer, 45),
        new("To", PinType.Integer, 70),
        new("Step", PinType.Integer, 95),
    ];

    // v5.0 §7: control-flow nodes have NO data output pins. The two Exec arms
    // (LoopBody/LoopEnd) are structural control edges implied by the ForLoop op —
    // they are rendered by the BP layer from the op's Targets, not from OutputPorts.
    public IReadOnlyList<PortSpec> OutputPorts => [];

    public FlowControlStatement ParseInvocation(BSCall call, int sourceLine)
    {
        var args = call.Args;
        var from = args.ElementAtOrDefault(0)?.SourceText ?? "0";
        var to = args.ElementAtOrDefault(1)?.SourceText ?? "0";
        var step = args.ElementAtOrDefault(2)?.SourceText ?? "1";
        var indexName = args.ElementAtOrDefault(3)?.AsStringLiteral() ?? "i";
        var bodyBlock = args.ElementAtOrDefault(4)?.AsStringLiteral() ?? "";
        var endBlock = args.ElementAtOrDefault(5)?.AsStringLiteral() ?? "";
        return new FlowControlStatement
        {
            LineNumber = sourceLine,
            SourceCode = call.SourceText,
            FunctionName = "ForLoop",
            ConditionExpression = from,
            FlowArguments = [from, to, step, indexName],
            Arms =
            [
                new() { PinName = "LoopBody", TargetBlockName = bodyBlock },
                new() { PinName = "LoopEnd",  TargetBlockName = endBlock },
            ],
        };
    }

    // ── Lowering: register indexName as an injected variable via the allocator so ──
    //    downstream name resolution (the backend's PubVar map) treats it as known.
    public IReadOnlyList<IrStatement> LowerToIr(
        BSCall call, IReadOnlyList<string> expandedArgs, string? assignedVar, LoweringContext ctx)
    {
        // expandedArgs carries [from, to, step, indexName, bodyBlock, endBlock].
        var indexName = expandedArgs.Count > 3 ? expandedArgs[3].Trim('"') : "i";
        if (!string.IsNullOrEmpty(indexName))
            ctx.Allocator.Register(indexName);

        return
        [
            new IrControlFlowStatement
            {
                Fingerprint = IrFingerprint.Compute("ForLoop", expandedArgs),
                Op = ControlFlowOp.ForLoop,
                FunctionName = "ForLoop",
                Arguments = expandedArgs.ToImmutableArray(),
                Targets =
                [
                    new IrControlFlowTarget("LoopBody", expandedArgs.Count > 4 ? expandedArgs[4].Trim('"') : ""),
                    new IrControlFlowTarget("LoopEnd",  expandedArgs.Count > 5 ? expandedArgs[5].Trim('"') : ""),
                ],
            },
        ];
    }

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        // IR carries from/to/step/indexName/body/end as flat arguments.
        var cf = (IrControlFlowStatement)stmt;
        var args = cf.Arguments;
        var from = args.Length > 0 ? ctx.ResolveArgument(args[0]) : ctx.Literal("0");
        var to = args.Length > 1 ? ctx.ResolveArgument(args[1]) : ctx.Literal("0");
        var step = args.Length > 2 ? ctx.ResolveArgument(args[2]) : ctx.Literal("1");
        var indexName = args.Length > 3 ? ctx.Literal(args[3].Trim('"')) : ctx.Literal("i");
        var bodyBlock = cf.Targets.Length > 0 ? cf.Targets[0].TargetBlockName : "";
        var endBlock = cf.Targets.Length > 1 ? cf.Targets[1].TargetBlockName : "";
        foreach (var s in ctx.EmitNextBlockAssignment("ForLoop",
            from, to, step, indexName, ctx.Literal(bodyBlock), ctx.Literal(endBlock)))
            yield return s;
    }

    // ── IBpReverseHandler: ForLoop renders as "Loop" on the BP canvas. This ──
    //    declaration removes the legacy BpEditApplier.DashboardToCfgName["Loop"] entry.
    public IReadOnlyCollection<string> BpNames => ["Loop"];

    public IrStatement BuildFromBp(BpNodeInfo node, string blockName)
    {
        // BP args carry the verbatim BS argument strings (from/to/step/indexName/blocks).
        var args = new List<string>();
        if (node.Arguments.TryGetValue("From", out var from)) args.Add(from);
        if (node.Arguments.TryGetValue("To", out var to)) args.Add(to);
        if (node.Arguments.TryGetValue("Step", out var step)) args.Add(step);
        if (node.Arguments.TryGetValue("IndexName", out var indexName)) args.Add(indexName);

        var loopBody = node.Arms.FirstOrDefault(a => a.PinName == "LoopBody");
        var loopEnd = node.Arms.FirstOrDefault(a => a.PinName == "LoopEnd");
        if (loopBody.TargetBlock is { Length: > 0 }) args.Add($"\"{loopBody.TargetBlock}\"");
        if (loopEnd.TargetBlock is { Length: > 0 }) args.Add($"\"{loopEnd.TargetBlock}\"");

        return new IrControlFlowStatement
        {
            Fingerprint = IrFingerprint.Compute("ForLoop", args),
            Op = ControlFlowOp.ForLoop,
            FunctionName = "ForLoop",
            Arguments = args.ToImmutableArray(),
            Targets =
            [
                new IrControlFlowTarget("LoopBody", loopBody.TargetBlock ?? ""),
                new IrControlFlowTarget("LoopEnd", loopEnd.TargetBlock ?? ""),
            ],
        };
    }
}

/// <summary>
/// Switch builtin — N-way dispatch by integer index.
/// BlockScript: <c>Switch(selector, "defaultBlock", "b0", "b1", ...)</c>.
/// Arms layout: [Default, 0, 1, ..., N-1]; the editor auto-appends arm N when pin N-1
/// is connected (see <see cref="OutputVariadic"/>).
/// </summary>
public sealed class SwitchFunction : IBuiltinFunction, IParserHandler, ICodeGenHandler
{
    public string Name => "Switch";
    public FunctionKind Kind => FunctionKind.ControlFlow;

    public IReadOnlyList<PortSpec> InputPorts =>
    [
        new("Exec", PinType.Execution, 20),
        new("Selector", PinType.Integer, 40),
    ];

    // v5.0 §7: control-flow nodes have NO data output pins. The Default + N arm
    // Exec pins are structural control edges; their growth is declared declaratively
    // via OutputVariadic (the editor appends arm N when pin N-1 connects). The fixed
    // initial template lives in OutputVariadic's StartIndex, not in OutputPorts.
    public IReadOnlyList<PortSpec> OutputPorts => [];

    /// <summary>Output variadic: when pin "0" is connected, append "1", "2", ...</summary>
    public VariadicPinSpec? OutputVariadic => new(string.Empty, 1, PinType.Execution);

    public FlowControlStatement ParseInvocation(BSCall call, int sourceLine)
    {
        var args = call.Args;
        var selector = args.ElementAtOrDefault(0)?.SourceText;
        // Arms: first arg after selector is Default, then 0, 1, ..., N-1.
        var arms = new List<BranchArm>();
        for (int i = 1; i < args.Count; i++)
        {
            var blockName = args[i]?.AsStringLiteral() ?? "";
            var pinName = i == 1 ? "Default" : (i - 2).ToString();
            arms.Add(new BranchArm { PinName = pinName, TargetBlockName = blockName });
        }
        if (arms.Count == 0)
            arms.Add(new BranchArm { PinName = "Default", TargetBlockName = "" });
        return new FlowControlStatement
        {
            LineNumber = sourceLine,
            SourceCode = call.SourceText,
            FunctionName = "Switch",
            ConditionExpression = selector ?? "",
            FlowArguments = selector is not null ? [selector] : [],
            Arms = arms,
        };
    }

    public IEnumerable<StatementSyntax> EmitCSharp(IrStatement stmt, CodeGenContext ctx)
    {
        var cf = (IrControlFlowStatement)stmt;
        // G.NextBlock = G.Switch(selector, "default", "b0", "b1", ...); break;
        var selExpr = ctx.Parse(cf.Arguments.Length > 0 ? cf.Arguments[0] : "0");
        var emitArgs = new List<ExpressionSyntax> { selExpr };
        emitArgs.AddRange(cf.Targets.Select(t => (ExpressionSyntax)ctx.Literal(t.TargetBlockName ?? "")));
        foreach (var s in ctx.EmitNextBlockAssignment("Switch", emitArgs.ToArray()))
            yield return s;
    }
}
