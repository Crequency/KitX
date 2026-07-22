namespace KitX.WorkflowV6.Lens.KsTextLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// KsLowerer — KsProgram (KS AST) → immutable Workflow (IR).
//
// Mostly a 1:1 structural transform: KsConstBlock/KsVarBlock → Workflow.Constants/
// GlobalVars; KsIf → IfStatement; KsForEach → ForEachStatement; KsWhile →
// WhileStatement; KsBreak/KsContinue/KsExit → their IR kinds; KsPipeline →
// PipelineStatement (carrying the structured KsNode sources + segments).
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
/// Lowers a parsed <see cref="KsProgram"/> AST into an immutable <see cref="Workflow"/>.
/// Pure: the same AST always yields the same IR. Does NOT flatten pipelines or
/// allocate PubVar capacitors — the IR keeps pipelines as structured AST.
/// </summary>
internal sealed class KsLowerer
{
    private readonly BuiltinFunctionRegistry? _registry;
    private HashSet<string> _helperNames = new(StringComparer.Ordinal);

    public KsLowerer(BuiltinFunctionRegistry? registry = null) => _registry = registry;

    /// <summary>
    /// Lowers <paramref name="program"/> into a <see cref="Workflow"/>. Helper
    /// functions are carried onto the workflow for downstream codegen. PubVar
    /// types are inferred from the var block declarations (discussion notes §十二-F).
    /// </summary>
    public (Workflow Ir, LoweringResult Result) Lower(
        KsProgram program,
        IReadOnlyList<HelperFunction> helpers)
    {
        // Build a set of helper function names for segment-tap disambiguation:
        // `5 > Double > Print` — "Double" has no parens but is a helper, not a variable.
        var helperNames = new HashSet<string>(helpers.Select(h => h.Name), StringComparer.Ordinal);
        _helperNames = helperNames;
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

        // ── Type inference: override PubVar types from pipeline assignments ──
        InferVarTypesFromPipelines(body, pubVarTypes);

        // Propagate inferred types back into the IR's GlobalVars so that downstream
        // consumers (StructuredRoslynBackend) pick up the corrected types.
        foreach (var (name, inferredType) in pubVarTypes)
        {
            if (globalVars.TryGetValue(name, out var gv) && gv.Type != inferredType)
                globalVars[name] = gv with { Type = inferredType };
        }

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

    private ImmutableArray<Statement> LowerStatements(IReadOnlyList<KsStatement> statements)
    {
        var builder = ImmutableArray.CreateBuilder<Statement>(statements.Count);
        foreach (var s in statements)
            builder.Add(LowerStatement(s));
        return builder.ToImmutable();
    }

    private Statement LowerStatement(KsStatement stmt)
    {
        Statement ir = stmt switch
        {
            KsPipeline pipe => LowerPipeline(pipe),
            KsIf iff => WithFingerprint(new IfStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Condition = iff.Condition,
                ThenBody = LowerStatements(iff.ThenBody),
                ElseBody = LowerStatements(iff.ElseBody),
                SourceLine = iff.SourceLine,
                Comment = null,
            }),
            KsSwitch sw => WithFingerprint(new SwitchStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Selector = sw.Selector,
                Arms = LowerArms(sw.Arms),
                Default = LowerStatements(sw.Default),
                SourceLine = sw.SourceLine,
                Comment = null,
            }),
            KsForEach fe => WithFingerprint(new ForEachStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Source = fe.Source,
                ItemName = fe.ItemName,
                ItemType = PinType.Any,  // type inference is filled in by Phase 4 codegen
                Body = LowerStatements(fe.Body),
                SourceLine = fe.SourceLine,
                Comment = null,
            }),
            KsWhile ws => WithFingerprint(new WhileStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                Condition = ws.Condition,
                Body = LowerStatements(ws.Body),
                SourceLine = ws.SourceLine,
                Comment = null,
            }),
            KsBreak => WithFingerprint(new BreakStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                SourceLine = stmt.SourceLine,
                Comment = null,
            }),
            KsContinue => WithFingerprint(new ContinueStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                SourceLine = stmt.SourceLine,
                Comment = null,
            }),
            KsExit => WithFingerprint(new ExitStatement
            {
                Fingerprint = Fingerprint.Compute("placeholder"),
                SourceLine = stmt.SourceLine,
                Comment = null,
            }),
            _ => throw new InvalidOperationException($"Unknown KS statement kind: {stmt.GetType().Name}"),
        };
        return ir;
    }

    /// <summary>Replaces the placeholder fingerprint with the real structural one.</summary>
    private static Statement WithFingerprint(Statement stmt) =>
        stmt with { Fingerprint = Fingerprint.Compute(stmt) };

    private ImmutableArray<ImmutableArray<Statement>> LowerArms(ImmutableArray<ImmutableArray<KsStatement>> arms)
    {
        var builder = ImmutableArray.CreateBuilder<ImmutableArray<Statement>>(arms.Length);
        foreach (var arm in arms)
            builder.Add(LowerStatements(arm));
        return builder.ToImmutable();
    }

    private Statement LowerPipeline(KsPipeline pipe)
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

    private Segment LowerSegment(KsPipelineSegment seg)
    {
        var args = ImmutableArray.CreateRange(seg.Args);
        var rawArgs = ImmutableArray.CreateRange(seg.RawArgs);
        // Disambiguate: a bare name without parens is a variable tap UNLESS it's a
        // known helper function (helpers are passed externally; Parser can't know).
        bool isVarTap = seg.IsVariableTap && !_helperNames.Contains(seg.Target);
        return new Segment
        {
            Target = seg.Target,
            Arguments = args,
            RawArguments = rawArgs,
            IsVariableTap = isVarTap,
        };
    }

    /// <summary>
    /// Walking the body tree, inspects pipeline statements for variable-tap assignments.
    /// When a pipeline like <c>... > Func > varName</c> assigns to a PubVar, and the
    /// function's return type is known from the registry, overrides the variable's
    /// declared type (e.g. int → bool for Compare).
    /// </summary>
    private void InferVarTypesFromPipelines(ImmutableArray<Statement> body, Dictionary<string, string> pubVarTypes)
    {
        foreach (var stmt in body)
            InferVarTypes(stmt, pubVarTypes);
    }

    private void InferVarTypes(Statement stmt, Dictionary<string, string> pubVarTypes)
    {
        if (_registry is null) return;
        if (stmt is PipelineStatement p && p.Segments.Length >= 2)
        {
            // Look for pattern: Segment_N-1 is a function call, Segment_N is a variable tap.
            var lastSeg = p.Segments[^1];
            var prevSeg = p.Segments[^2];
            if (!_registry.Contains(lastSeg.Target) && lastSeg.Arguments.Length == 0
                && pubVarTypes.ContainsKey(lastSeg.Target))
            {
                // lastSeg.Target is a PubVar name, prevSeg is a function call.
                var funcName = prevSeg.Target;
                var func = _registry.Get(funcName);
                if (func is not null)
                {
                    var csharpType = MapBuiltinReturnTypeToCSharp(func);
                    if (csharpType is not null)
                        pubVarTypes[lastSeg.Target] = csharpType;
                }
            }
        }
        // Recurse into structured bodies.
        switch (stmt)
        {
            case IfStatement iff:
                InferVarTypesFromPipelines(iff.ThenBody, pubVarTypes);
                InferVarTypesFromPipelines(iff.ElseBody, pubVarTypes);
                break;
            case ForEachStatement fe:
                InferVarTypesFromPipelines(fe.Body, pubVarTypes);
                break;
            case WhileStatement ws:
                InferVarTypesFromPipelines(ws.Body, pubVarTypes);
                break;
        }
    }

    private static string? MapBuiltinReturnTypeToCSharp(IBuiltinFunction func)
    {
        if (func.OutputPorts.Count == 0) return null;
        return func.OutputPorts[0].Type switch
        {
            global::KitX.Core.Contract.Workflow.PinType.Boolean => "bool",
            global::KitX.Core.Contract.Workflow.PinType.Integer => "int",
            global::KitX.Core.Contract.Workflow.PinType.Double => "double",
            global::KitX.Core.Contract.Workflow.PinType.String => "string",
            _ => null,
        };
    }
}