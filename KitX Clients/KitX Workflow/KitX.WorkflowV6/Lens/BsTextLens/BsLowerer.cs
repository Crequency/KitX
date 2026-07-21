namespace KitX.WorkflowV6.Lens.BsTextLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// BsLowerer — BsProgram (BS AST) → immutable Workflow (IR).
//
// Mostly a 1:1 structural transform: BsConstBlock/BsVarBlock → Workflow.Constants/
// GlobalVars; BsIf → IfStatement; BsForEach → ForEachStatement; BsWhile →
// WhileStatement; BsBreak/BsContinue/BsExit → their IR kinds; BsPipeline →
// PipelineStatement (carrying the structured BsNode sources + segments).
//
// No pipeline flattening, no PubVar capacitor allocation, no nested-call expansion
// — those v5 smells are gone because the IR keeps pipelines as structured AST.
// The lowerer is therefore ~3x shorter than v5's BS2CFGConverter.
//
// The lowerer consults the <see cref="BuiltinFunctionRegistry"/> only for custom
// lowering handlers (per-builtin <see cref="ILoweringHandler"/>); the default
// path is the 1:1 transform. Control-flow primitives (if/switch/forEach/while/
// break/continue/exit) are NOT routed through the registry — they are first-class
// IR statement kinds per discussion notes §十二-K, so the lowerer builds them
// directly.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lowers a parsed <see cref="BsProgram"/> AST into an immutable <see cref="Workflow"/>.
/// Pure: the same AST always yields the same IR. Does NOT flatten pipelines or
/// allocate PubVar capacitors — the IR keeps pipelines as structured AST.
/// </summary>
internal sealed class BsLowerer
{
    private readonly BuiltinFunctionRegistry? _registry;

    public BsLowerer(BuiltinFunctionRegistry? registry = null) => _registry = registry;

    /// <summary>
    /// Lowers <paramref name="program"/> into a <see cref="Workflow"/>. Helper
    /// functions are carried onto the workflow for downstream codegen. PubVar
    /// types are inferred from the var block declarations (discussion notes §十二-F).
    /// </summary>
    public (Workflow Ir, LoweringResult Result) Lower(
        BsProgram program,
        IReadOnlyList<HelperFunction> helpers)
    {
        // ── Declarations ──
        var constants = ImmutableDictionary.CreateBuilder<string, Constant>();
        var globalVars = ImmutableDictionary.CreateBuilder<string, GlobalVar>();
        var pubVarTypes = new Dictionary<string, string>();

        if (program.ConstBlock is not null)
        {
            foreach (var d in program.ConstBlock.Declarations)
            {
                constants.Add(d.Name, new Constant
                {
                    Name = d.Name,
                    Type = d.Type,
                    InitialValueExpression = d.InitialValueExpression,
                });
                pubVarTypes[d.Name] = d.Type;
            }
        }
        if (program.VarBlock is not null)
        {
            foreach (var d in program.VarBlock.Declarations)
            {
                globalVars.Add(d.Name, new GlobalVar
                {
                    Name = d.Name,
                    Type = d.Type,
                    InitialValueExpression = d.InitialValueExpression,
                });
                pubVarTypes[d.Name] = d.Type;
            }
        }

        // ── Body ──
        var body = LowerStatements(program.Body);

        var ir = new Workflow
        {
            Body = body,
            Constants = constants.ToImmutable(),
            GlobalVars = globalVars.ToImmutable(),
            HelperFunctions = helpers.ToImmutableArray(),
        };

        var result = new LoweringResult
        {
            PubVarTypes = pubVarTypes,
            HelperReturnTypes = new Dictionary<string, string>(),
            InjectedVariableNames = new HashSet<string>(),
        };

        return (ir, result);
    }

    private ImmutableArray<Statement> LowerStatements(IReadOnlyList<BsStatement> statements)
    {
        var builder = ImmutableArray.CreateBuilder<Statement>(statements.Count);
        foreach (var s in statements)
            builder.Add(LowerStatement(s));
        return builder.ToImmutable();
    }

    private Statement LowerStatement(BsStatement stmt)
    {
        Statement ir = stmt switch
        {
            BsPipeline pipe => LowerPipeline(pipe),
            BsIf iff => WithFingerprint(new IfStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Condition = iff.Condition,
                ThenBody = LowerStatements(iff.ThenBody),
                ElseBody = LowerStatements(iff.ElseBody),
                SourceLine = iff.SourceLine,
                Comment = null,
            }),
            BsSwitch sw => WithFingerprint(new SwitchStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Selector = sw.Selector,
                Arms = LowerArms(sw.Arms),
                Default = LowerStatements(sw.Default),
                SourceLine = sw.SourceLine,
                Comment = null,
            }),
            BsForEach fe => WithFingerprint(new ForEachStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Source = fe.Source,
                ItemName = fe.ItemName,
                ItemType = PinType.Any,  // type inference is filled in by Phase 4 codegen
                Body = LowerStatements(fe.Body),
                SourceLine = fe.SourceLine,
                Comment = null,
            }),
            BsWhile ws => WithFingerprint(new WhileStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Condition = ws.Condition,
                Body = LowerStatements(ws.Body),
                SourceLine = ws.SourceLine,
                Comment = null,
            }),
            BsBreak => WithFingerprint(new BreakStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                SourceLine = stmt.SourceLine,
                Comment = null,
            }),
            BsContinue => WithFingerprint(new ContinueStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                SourceLine = stmt.SourceLine,
                Comment = null,
            }),
            BsExit => WithFingerprint(new ExitStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                SourceLine = stmt.SourceLine,
                Comment = null,
            }),
            _ => throw new InvalidOperationException($"Unknown BS statement kind: {stmt.GetType().Name}"),
        };
        return ir;
    }

    /// <summary>Replaces the placeholder fingerprint with the real structural one.</summary>
    private static Statement WithFingerprint(Statement stmt) =>
        stmt with { Fingerprint = Fingerprint.Compute(stmt) };

    private ImmutableArray<ImmutableArray<Statement>> LowerArms(ImmutableArray<ImmutableArray<BsStatement>> arms)
    {
        var builder = ImmutableArray.CreateBuilder<ImmutableArray<Statement>>(arms.Length);
        foreach (var arm in arms)
            builder.Add(LowerStatements(arm));
        return builder.ToImmutable();
    }

    private Statement LowerPipeline(BsPipeline pipe)
    {
        var sources = ImmutableArray.CreateRange(pipe.Sources);
        var segments = ImmutableArray.CreateRange(pipe.Segments.Select(LowerSegment));
        var stmt = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = sources,
            Segments = segments,
            SourceLine = pipe.SourceLine,
            Comment = null,
        };
        return WithFingerprint(stmt);
    }

    private Segment LowerSegment(BsPipelineSegment seg)
    {
        var args = ImmutableArray.CreateRange(seg.Args);
        var rawArgs = ImmutableArray.CreateRange(seg.RawArgs);
        return new Segment
        {
            Target = seg.Target,
            Arguments = args,
            RawArguments = rawArgs,
            IsVariableTap = seg.IsVariableTap,
        };
    }
}