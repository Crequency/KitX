namespace KitX.Workflow.Lens.BsTextLens;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Diff;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// BsTextLens — the bidirectional bridge between the IR and BlockScript text.
//
// Implements ILens<string, string>: the "view" is BS source text, the "delta" is the
// edited text. This is the concrete Lens the editor, the round-trip tests, and the
// .kcs save path use. It wires the three Phase-5 components together:
//
//   • Project(ir)  → BsRenderer.Render(ir): IR → canonical BS text (a pure read).
//   • Parse(text)  → BsTextLensParser + BsLowerer: BS text → immutable IrWorkflow.
//   • Diff(...)    → re-parse the edited text, then IrDiffer.Compute for the delta.
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
    /// baseline IR. Re-parses the edited BS text into a fresh IR, then diffs it
    /// against <paramref name="baseline"/> via <see cref="IrDiffer.Compute"/> — a
    /// content-addressed (IrFingerprint-keyed) delta that survives a re-parse.
    /// </summary>
    /// <param name="baseline">The IR the editor is currently projecting from.</param>
    /// <param name="delta">The edited BS source text.</param>
    public IrDiff Diff(IrWorkflow baseline, string delta)
        => Diff(baseline, delta, helperFunctions: null);

    /// <summary>
    /// Folds an edited-text delta back into an <see cref="IrDiff"/> against the
    /// baseline IR, with an explicit helper-function set for the re-parse. Re-parses
    /// the edited BS text into a fresh IR, then diffs it against
    /// <paramref name="baseline"/> via <see cref="IrDiffer.Compute"/>.
    /// </summary>
    /// <param name="baseline">The IR the editor is currently projecting from.</param>
    /// <param name="delta">The edited BS source text.</param>
    /// <param name="helperFunctions">Helper functions available to the re-parse.</param>
    public IrDiff Diff(IrWorkflow baseline, string delta, IReadOnlyList<HelperFunction>? helperFunctions)
    {
        var newIr = Parse(delta, helperFunctions);
        return IrDiffer.Compute(baseline, newIr);
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
