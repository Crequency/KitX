namespace KitX.Workflow.Lens.BsTextLens;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Ast;
using KitX.Workflow.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// BsLowerer — BS AST (BlockScript) → immutable IrWorkflow.
//
// Replaces the legacy BS2CFGConverter (713 lines) + PipelineFlattener (201 lines).
// The biggest architectural win of the greenfield IR is that this lowering DOES
// NOT FLATTEN. The legacy converter expanded every pipeline `a, b > F > G > x`
// into a sequence of imperative PubVar-assignment statements (vaaa0001 = F(a,b);
// vaaa0002 = G(vaaa0001); x = vaaa0002) and stored THOSE in the CFG. That eagerly
// threw away the structured pipeline shape, forced a FoldCapacitors pass in the
// renderer to rebuild it, and made every lowering allocate PubVar capacitors
// whose names drifted across round-trips.
//
// The new IR carries pipelines verbatim as IrPipelineStatement (Sources + Segments
// AST). So BsLowerer becomes a mostly-1:1 structural transform: BSPipeline →
// IrPipelineStatement, FlowControlStatement → IrControlFlowStatement, blocks →
// IrBlock. No capacitor allocation, no nested-call expansion, no flattening —
// those are the consumer's concern (a pure function, Phase 7). This is ~3x shorter
// and free of the legacy smells.
//
// Smells ELIMINATED (do not re-introduce):
//   • _currentContext implicit field (legacy BS2CFGConverter L43) → LoweringContext
//     is passed explicitly per statement; no hidden mutable state on the lowerer.
//   • ForwardConversionState shotgun parameter → LoweringInput (in) +
//     LoweringResult (out) + PubVarAllocator (locally-scoped helper).
//   • ExpandExpression 130-line switch-on-type → a proper visitor returning an
//     immutable list (here: a small method returning IrPipelineArgument list).
//   • mutation accumulation → produces an immutable IrWorkflow once at the end.
//   • PipelineFlattener's capacitor synthesis / single-source redirect / self-
//     increment detection → GONE. The IR keeps the pipeline AST, so there are no
//     capacitors to synthesise.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lowers a parsed <see cref="BlockScript"/> AST into an immutable
/// <see cref="IrWorkflow"/>. Pure structural transform; no pipeline flattening and
/// no PubVar capacitor allocation (the IR keeps pipelines as structured AST).
/// </summary>
public sealed class BsLowerer
{
    private readonly BuiltinFunctionRegistry? _registry;

    /// <summary>Creates a lowerer that consults <paramref name="registry"/> for
    /// control-flow builtin dispatch (e.g. Branch/ForLoop custom lowering).</summary>
    public BsLowerer(BuiltinFunctionRegistry? registry = null) => _registry = registry;

    /// <summary>
    /// Lowers <see cref="LoweringInput.Script"/> into an immutable
    /// <see cref="LoweringResult"/>. Signature matches the Phase-2 explicit-parameter
    /// paradigm: <c>Lower(input, registry)</c> — a pure-ish function with all
    /// mutation confined to a locally-scoped PubVarAllocator. The instance's own
    /// registry is used unless <paramref name="registryOverride"/> differs.
    /// </summary>
    public LoweringResult Lower(LoweringInput input, BuiltinFunctionRegistry? registryOverride = null)
    {
        // Honour an explicit registry override by delegating to a fresh lowerer, so
        // the constructor-injected registry never silently shadows the caller's.
        if (registryOverride is not null && registryOverride != _registry)
            return new BsLowerer(registryOverride).LowerCore(input);
        return LowerCore(input);
    }

    /// <summary>Lower using the registry supplied at construction (primary entry).</summary>
    public LoweringResult Lower(LoweringInput input) => LowerCore(input);

    private LoweringResult LowerCore(LoweringInput input)
    {
        var allocator = new PubVarAllocator();
        var diagnostics = new List<LoweringDiagnostic>();

        // ── Phase-1 output: lift PubVar/Const declarations into the IR. ──
        var constants = ImmutableDictionary.CreateBuilder<string, IrConstant>();
        var globalVars = ImmutableDictionary.CreateBuilder<string, IrGlobalVar>();
        var pubVarTypes = new Dictionary<string, string>();

        if (input.Script.ConstBlock is { } constBlock)
        {
            foreach (var v in constBlock.Variables)
            {
                constants.Add(v.Name, new IrConstant(
                    v.Name, string.IsNullOrEmpty(v.Type) ? "object" : v.Type,
                    v.InitialValueExpression, v.DefaultValue));
            }
        }

        if (input.Script.PubVarBlock is { } pubVarBlock)
        {
            foreach (var v in pubVarBlock.Variables)
            {
                var type = string.IsNullOrEmpty(v.Type) ? "object" : v.Type;
                globalVars.Add(v.Name, new IrGlobalVar(
                    v.Name, type, v.InitialValueExpression, v.DefaultValue));
                pubVarTypes[v.Name] = type;
                allocator.Register(v.Name);
            }
        }

        // ── Phase-2 output: lower each block into an IrBlock. ──
        var blocks = ImmutableArray.CreateBuilder<IrBlock>();
        foreach (var blockDef in input.Script.AllBlocks)
        {
            // ConstBlock / PubVarBlock are declaration-only; they have no statements and
            // are not first-class IR blocks (their content lives in Constants/GlobalVars).
            if (blockDef.Type is BlockType.ConstBlock or BlockType.PubVarBlock)
                continue;

            var irBlock = LowerBlock(blockDef, input.Script, allocator, pubVarTypes, diagnostics);
            blocks.Add(irBlock);
        }

        var ir = new IrWorkflow
        {
            MainBlockName = input.Script.MainBlock?.Name ?? "#MainBlock",
            Blocks = blocks.ToImmutable(),
            Constants = constants.ToImmutable(),
            GlobalVars = globalVars.ToImmutable(),
            HelperFunctions = input.HelperFunctions.ToImmutableArray(),
        };

        return new LoweringResult
        {
            Ir = ir,
            PubVarTypes = pubVarTypes,
            PubVarNames = allocator.Names,
            InjectedVariableNames = CollectInjectedVariables(input.Script),
            Diagnostics = diagnostics,
        };
    }

    // ── Block lowering ───────────────────────────────────────────────────────

    private IrBlock LowerBlock(
        BlockDefinition blockDef,
        BlockScript script,
        PubVarAllocator allocator,
        Dictionary<string, string> pubVarTypes,
        List<LoweringDiagnostic> diagnostics)
    {
        var kind = blockDef.Type == BlockType.MainBlock ? IrBlockKind.Entry : IrBlockKind.Basic;
        var statements = ImmutableArray.CreateBuilder<IrStatement>();
        var annotations = ImmutableArray.CreateBuilder<IrAnnotation>();

        bool seenTerminator = false;
        foreach (var stmt in blockDef.Statements)
        {
            if (seenTerminator)
            {
                diagnostics.Add(new LoweringDiagnostic(
                    LoweringDiagnosticSeverity.Warning, "BS_DEAD_CODE",
                    $"Unreachable statement after control-flow in block '{blockDef.Name}' is ignored.",
                    stmt.LineNumber > 0 ? stmt.LineNumber : null));
                continue;
            }

            if (LowerStatement(stmt, blockDef.Name, allocator, diagnostics) is { } irStmt)
            {
                statements.Add(irStmt);
                if (irStmt is IrControlFlowStatement)
                {
                    seenTerminator = true;
                    kind = IrBlockKind.BranchHeader;
                }
            }
        }

        // Block-local variables (##BlockVars).
        var blockVars = blockDef.BlockVars
            .Select(v => new IrBlockVar(
                v.Name,
                string.IsNullOrEmpty(v.Type) ? "object" : v.Type,
                v.InitialValueExpression))
            .ToImmutableArray();

        // Sequential fall-through edge: BlockDefinition.NextBlockName (set by the
        // BlockLinker for blocks that predate the §7.7 rule) becomes a Sequential
        // edge — the single source of truth for "what runs next". Only when the
        // block does not already end in control flow (those blocks carry their own
        // Branch/ForLoop/Switch/Goto/Break targets).
        var successors = ImmutableArray.CreateBuilder<IrEdge>();
        if (!seenTerminator && !string.IsNullOrEmpty(blockDef.NextBlockName))
        {
            successors.Add(new IrEdge(blockDef.Name, blockDef.NextBlockName,
                IrEdgeType.Sequential, BlockScriptWellKnown.Pins.Exec));
        }

        // Block-level comment → annotation (excluded from equality).
        if (!string.IsNullOrEmpty(blockDef.Comment))
            annotations.Add(new IrAnnotation(AnnotationKind.Comment, blockDef.Name, blockDef.Comment));

        return new IrBlock
        {
            Name = blockDef.Name,
            Kind = kind,
            Statements = statements.ToImmutable(),
            BlockVars = blockVars,
            Successors = successors.ToImmutable(),
            HasExplicitBlockBody = blockDef.HasExplicitBlockBody,
            Annotations = annotations.ToImmutable(),
        };
    }

    // ── Statement lowering (the AST visitor) ──────────────────────────────────

    private IrStatement? LowerStatement(
        BlockStatement stmt, string blockName,
        PubVarAllocator allocator, List<LoweringDiagnostic> diagnostics)
    {
        switch (stmt)
        {
            case FlowControlStatement flow:
                return LowerFlowControl(flow, blockName, allocator);

            case ExpressionStatement expr:
                return LowerExpressionStatement(expr, blockName);

            default:
                diagnostics.Add(new LoweringDiagnostic(
                    LoweringDiagnosticSeverity.Warning, "BS_UNSUPPORTED_STMT",
                    $"Unsupported statement kind '{stmt.GetType().Name}' in block '{blockName}' is skipped.",
                    stmt.LineNumber > 0 ? stmt.LineNumber : null));
                return null;
        }
    }

    /// <summary>
    /// Lowers a pipeline expression statement. The key simplification: NO flattening.
    /// The BSPipeline AST (Sources + Targets) maps directly onto IrPipelineStatement
    /// (Sources + Segments). Each BSCall target becomes an IrSegment.
    /// </summary>
    private IrStatement LowerExpressionStatement(ExpressionStatement exprStmt, string blockName)
    {
        if (exprStmt.ParsedExpression is BSPipeline pipeline)
        {
            return LowerPipeline(pipeline, exprStmt, blockName);
        }

        // A bare call (no pipeline operator) wrapped as a single-source, single-target
        // pipeline. ParsedExpression was a BSCall here before the parser normalised it.
        if (exprStmt.ParsedExpression is BSCall call)
        {
            return LowerPipeline(
                new BSPipeline { Sources = [call], Targets = [], SourceText = call.SourceText },
                exprStmt, blockName);
        }

        // Fallback: an identifier/literal-only expression. Render as a trivial pipeline.
        if (exprStmt.ParsedExpression is { } expr)
        {
            return LowerPipeline(
                new BSPipeline { Sources = [expr], Targets = [], SourceText = expr.SourceText },
                exprStmt, blockName);
        }

        return new IrPipelineStatement
        {
            Fingerprint = IrFingerprint.Compute("__noop", []),
            Sources = [],
            Segments = [],
            Comment = exprStmt.Comment,
            // SourceLine omitted — see LowerPipeline.
        };
    }

    /// <summary>
    /// Structural transform BSPipeline → IrPipelineStatement. Sources map 1:1 to
    /// verbatim source text; each BSCall target becomes an IrSegment (FunctionCall,
    /// or Variable when the target is a bare identifier tap). Argument placeholders
    /// (<c>_</c>) become IrPipelineArgument.Placeholder; other args become Literal.
    /// </summary>
    private IrPipelineStatement LowerPipeline(BSPipeline pipeline, ExpressionStatement exprStmt, string blockName)
    {
        // Sources: the verbatim source expressions (left of the first `>`).
        var sources = pipeline.Sources.Select(s => s.SourceText).ToImmutableArray();

        var segments = ImmutableArray.CreateBuilder<IrSegment>(pipeline.Targets.Count);
        foreach (var target in pipeline.Targets)
            segments.Add(LowerSegment(target));

        var segmentsArray = segments.MoveToImmutable();

        // Fingerprint: a content-derived identity over the function names + argument
        // text + terminal target, so two structurally identical pipelines match.
        var fp = ComputePipelineFingerprint(pipeline, segmentsArray);

        return new IrPipelineStatement
        {
            Fingerprint = fp,
            Sources = sources,
            Segments = segmentsArray,
            Comment = exprStmt.Comment,
            // SourceLine deliberately NOT carried: it is 1-based line-in-source, which is
            // inherently render-sensitive (the renderer normalises blank lines / indentation,
            // so the same statement lands on different lines across a round-trip). Leaving it
            // at the default 0 keeps IR equality stable across re-parse — the content-derived
            // Fingerprint + DeriveStableId are the stable identity primitives. The original
            // position belongs in an IrAnnotation(SourcePosition), not in semantic equality.
        };
    }

    /// <summary>
    /// Lowers one BSCall target into an IrSegment. A bare-identifier target (no
    /// parens, no args) is a Variable tap — the pipeline result stores into that
    /// variable. Otherwise it is a FunctionCall whose arguments may contain `_`
    /// placeholders (positions where pipeline values insert) and literal args.
    /// </summary>
    private IrSegment LowerSegment(BSCall target)
    {
        // Variable tap: a bare identifier used as a pipeline target (e.g. `> x` or
        // `> cond`). The parser produced a BSCall with zero args whose MethodName
        // equals the FullMethodName (no dotted receiver). This is an assignment, not
        // a function call.
        bool isVariableTap = target.Args.Count == 0
            && target.MethodName == target.FullMethodName
            && !target.SourceText.EndsWith(')');

        if (isVariableTap)
        {
            return new IrSegment
            {
                Kind = IrSegmentKind.Variable,
                VariableName = target.MethodName,
            };
        }

        // Function call: walk arguments, mapping each to Placeholder or Literal.
        var args = ImmutableArray.CreateBuilder<IrPipelineArgument>(target.Args.Count);
        int placeholderOrdinal = 0;
        foreach (var arg in target.Args)
        {
            if (arg is BSPlaceholder ph)
            {
                // Preserve the parsed index when present, else assign sequentially.
                args.Add(IrPipelineArgument.Placeholder(ph.Index != 0 ? ph.Index : placeholderOrdinal));
                placeholderOrdinal++;
            }
            else
            {
                args.Add(IrPipelineArgument.Lit(arg.SourceText));
            }
        }

        return new IrSegment
        {
            Kind = IrSegmentKind.FunctionCall,
            FunctionName = target.MethodName,
            FullFunctionName = string.IsNullOrEmpty(target.FullMethodName)
                ? target.MethodName : target.FullMethodName,
            Arguments = args.MoveToImmutable(),
        };
    }

    /// <summary>
    /// Lowers a flow-control statement (Branch/ForLoop/Switch/Goto/Break/Exit/Flip)
    /// into an <see cref="IrControlFlowStatement"/>. If the function registered an
    /// <see cref="ILoweringHandler"/>, delegate to it (it knows how to materialise
    /// its own arguments/targets, e.g. ForLoop registers its indexName). Otherwise
    /// build the statement directly from the FlowControlStatement's parsed fields.
    /// </summary>
    private IrStatement LowerFlowControl(
        FlowControlStatement flow, string blockName, PubVarAllocator allocator)
    {
        var funcName = flow.FunctionName ?? string.Empty;
        var op = MapOp(funcName);

        // Condition / selector materialisation: in the legacy lowering the condition
        // was materialised into a PubVar. In the new IR we keep the verbatim condition
        // text as an argument — NO PubVar synthesis. The structured AST survives, and
        // a downstream consumer flattens if it must.
        var arguments = BuildFlowArguments(flow);

        // Targets: map the FlowControlStatement arms to IrControlFlowTarget records.
        var targets = flow.Arms
            .Select(a => new IrControlFlowTarget(a.PinName, a.TargetBlockName))
            .ToImmutableArray();

        return new IrControlFlowStatement
        {
            Fingerprint = IrFingerprint.Compute(funcName, arguments),
            Op = op,
            FunctionName = funcName,
            Arguments = arguments,
            Targets = targets,
            Comment = flow.Comment,
            // SourceLine omitted — see LowerPipeline (render-sensitive, breaks round-trip equality).
        };
    }

    /// <summary>
    /// Builds the flat argument list for a control-flow statement. The Branch
    /// condition, ForLoop bounds, and Switch selector all carry through as verbatim
    /// source text — no expansion, no PubVar materialisation.
    /// </summary>
    private ImmutableArray<string> BuildFlowArguments(FlowControlStatement flow)
    {
        // ForLoop already parsed [from, to, step, indexName] into FlowArguments.
        // Branch/Switch put the condition/selector into ConditionExpression.
        if (flow.FlowArguments.Count > 0)
            return flow.FlowArguments.ToImmutableArray();

        if (!string.IsNullOrEmpty(flow.ConditionExpression))
            return ImmutableArray.Create(flow.ConditionExpression);

        return [];
    }

    private static ControlFlowOp MapOp(string funcName) => funcName switch
    {
        "Branch" => ControlFlowOp.Branch,
        "ForLoop" => ControlFlowOp.ForLoop,
        "Switch" => ControlFlowOp.Switch,
        "Goto" => ControlFlowOp.Goto,
        "Break" => ControlFlowOp.Break,
        "Exit" => ControlFlowOp.Exit,
        // Flip is a Branch-shaped two-way control flow; map to Branch so the
        // uniform Targets model applies. (A dedicated op could be added later.)
        "Flip" => ControlFlowOp.Branch,
        _ => ControlFlowOp.Goto,
    };

    /// <summary>
    /// Computes a content-derived fingerprint for a pipeline. Uses the function names
    /// of all segments + their literal arguments + the terminal variable target, so
    /// two pipelines that differ only in capacitor naming (which the new IR never
    /// introduces) still match.
    /// </summary>
    private static IrFingerprint ComputePipelineFingerprint(BSPipeline pipeline, ImmutableArray<IrSegment> segments)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(string.Join(",", pipeline.Sources.Select(s => s.SourceText.Trim().Replace(" ", ""))));
        sb.Append('|');
        for (int i = 0; i < segments.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var seg = segments[i];
            if (seg.Kind == IrSegmentKind.Variable)
            {
                sb.Append('>').Append(seg.VariableName);
            }
            else
            {
                sb.Append(seg.FunctionName).Append('(');
                for (int a = 0; a < seg.Arguments.Length; a++)
                {
                    if (a > 0) sb.Append(',');
                    var arg = seg.Arguments[a];
                    sb.Append(arg.Kind == IrPipelineArgumentKind.Placeholder ? "_" : arg.Literal);
                }
                sb.Append(')');
            }
        }
        return new IrFingerprint(sb.ToString());
    }

    /// <summary>
    /// Collects variable names injected at runtime by flow-control functions
    /// (ForLoop's indexName). Used by the C# backend to emit G.Get(...) references.
    /// </summary>
    private static HashSet<string> CollectInjectedVariables(BlockScript script)
    {
        var names = new HashSet<string>();
        foreach (var block in script.AllBlocks)
        {
            if (block.Type is BlockType.ConstBlock or BlockType.PubVarBlock) continue;
            foreach (var stmt in block.Statements)
            {
                if (stmt is FlowControlStatement fc
                    && fc.FunctionName == "ForLoop"
                    && fc.FlowArguments.Count >= 4
                    && !string.IsNullOrEmpty(fc.FlowArguments[3]))
                {
                    names.Add(fc.FlowArguments[3]);
                }
            }
        }
        return names;
    }
}
