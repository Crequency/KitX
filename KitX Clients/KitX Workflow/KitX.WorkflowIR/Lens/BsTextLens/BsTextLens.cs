namespace KitX.WorkflowIR.Lens.BsTextLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowIR.Builtin;
using KitX.WorkflowIR.Ir;
using KitX.WorkflowIR.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// BsTextLens — the bidirectional bridge between the IR and BlockScript text.
//
// Implements ILens<string, string>: the "view" is BS source text, the "delta" is the
// edited text. This is the concrete Lens the editor, the round-trip tests, and the
// .kcs save path use. It wires the three Phase-5 components together:
//
//   • Project(ir)  → BsRenderer.Render(ir): IR → canonical BS text (a pure read).
//   • Parse(text)  → BsTextLensParser + BsLowerer: BS text → immutable IrWorkflow.
//   • Diff(...)    → Phase 6 (placeholder returns an empty IrDiff for now).
//
// The registry is constructor-injected (NOT a static singleton — that was a legacy
// smell in CFGRenderer.BuiltinFunctionRegistry.Instance). One lens per DI scope.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The BS-text lens: projects <see cref="IrWorkflow"/> ↔ BlockScript source text.
/// A single registry is injected and shared by the parser and lowerer.
/// </summary>
public sealed class BsTextLens : ILens<string, string>
{
    private readonly BuiltinFunctionRegistry? _registry;
    private readonly BsRenderer _renderer;

    /// <summary>Creates a lens with no builtin registry (bare-call parsing only).</summary>
    public BsTextLens() : this(null) { }

    /// <summary>Creates a lens backed by <paramref name="registry"/> for control-flow dispatch.</summary>
    public BsTextLens(BuiltinFunctionRegistry? registry)
    {
        _registry = registry;
        _renderer = new BsRenderer();
    }

    // ── ILens<string, string> ──────────────────────────────────────────────────

    /// <summary>Projects the IR into canonical BlockScript source text (a pure read).</summary>
    public string Project(IrWorkflow ir) => _renderer.Render(ir);

    /// <summary>
    /// Folds an edited-text delta back into an <see cref="IrDiff"/> against the
    /// baseline IR. Phase 6 implements the diff engine; this returns an empty
    /// placeholder so the lens compiles today.
    /// </summary>
    public IrDiff Diff(IrWorkflow baseline, string delta)
    {
        // Phase 6: re-parse `delta`, diff against `baseline` by IrFingerprint, return ops.
        _ = baseline; _ = delta;
        return new IrDiff();
    }

    // ── Parse convenience (text → IR) ──────────────────────────────────────────

    /// <summary>
    /// Parses BS source text into an immutable <see cref="IrWorkflow"/>. Convenience
    /// for the full pipeline: text → BlockScript AST (BsTextLensParser) → IR
    /// (BsLowerer). Returns the lowered IR, or throws when parsing fails outright.
    /// </summary>
    /// <param name="bsText">The BS source text to parse.</param>
    /// <param name="helperFunctions">Helper functions available to the script (default none).</param>
    public IrWorkflow Parse(string bsText, IReadOnlyList<HelperFunction>? helperFunctions = null)
        => ParseLowering(bsText, helperFunctions).Ir;

    /// <summary>
    /// Parses BS source text and returns the full <see cref="LoweringResult"/>
    /// (IR + PubVar types/names + diagnostics). Use this when you need diagnostics
    /// or the PubVar membership set rather than just the IR.
    /// </summary>
    public LoweringResult ParseLowering(string bsText, IReadOnlyList<HelperFunction>? helperFunctions = null)
    {
        var parse = new BsTextLensParser(_registry).Parse(bsText);
        if (!parse.IsSuccess || parse.Script is null)
        {
            throw new InvalidOperationException(
                $"Failed to parse BS text: {parse.ErrorMessage ?? "unknown error"}" +
                (parse.ErrorLine > 0 ? $" (line {parse.ErrorLine})" : ""));
        }

        var input = new LoweringInput
        {
            Script = parse.Script,
            HelperFunctions = helperFunctions ?? [],
        };
        return new BsLowerer(_registry).Lower(input);
    }
}
