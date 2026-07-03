namespace KitX.Workflow.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BpGraphLens — the bidirectional bridge between the IR and the Blueprint graph.
//
// Implements ILens<Blueprint, IReadOnlyList<BpEditAction>>: the "view" is a
// mutable Blueprint, the "delta" is a stream of BP canvas edits. This is the BP
// counterpart to BsTextLens: where BsTextLens projects IR ↔ BlockScript text,
// BpGraphLens projects IR ↔ the visual node graph.
//
//   • Project(ir)        → BpRenderer.Render(ir): IR → mutable Blueprint (a pure
//                          read of the IR into the Contract's mutable node shape).
//   • Diff(baseline,delta)→ BpEditTranslator: fold the canvas edit stream back
//                          into a content-addressed IrDiff. The caller (SyncService,
//                          Phase 9) is responsible for IrDiffApply.Apply.
//
// The registry is constructor-injected (NOT a static singleton — that was a
// legacy smell in CFGRenderer.BuiltinFunctionRegistry.Instance). One lens per DI
// scope. The renderer and translator share the same registry instance so their
// BP-name resolution stays in sync.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The BP-graph lens: projects <see cref="IrWorkflow"/> ↔ a mutable
/// <see cref="Blueprint"/>, folding BP canvas edits back into an
/// <see cref="IrDiff"/>. Wraps <see cref="BpRenderer"/> (Project) and
/// <see cref="BpEditTranslator"/> (Diff).
/// </summary>
public sealed class BpGraphLens : ILens<Blueprint, IReadOnlyList<BpEditAction>>
{
    private readonly BuiltinFunctionRegistry? _registry;
    private readonly BpRenderer _renderer;

    /// <summary>Creates a lens with no builtin registry (default node shapes only).</summary>
    public BpGraphLens() : this(null) { }

    /// <summary>Creates a lens backed by <paramref name="registry"/> for BP-name resolution and custom node templates.</summary>
    public BpGraphLens(BuiltinFunctionRegistry? registry)
    {
        _registry = registry;
        _renderer = new BpRenderer(registry);
    }

    // ── ILens<Blueprint, IReadOnlyList<BpEditAction>> ─────────────────────────

    /// <summary>
    /// Projects the IR into a mutable <see cref="Blueprint"/> (a pure read).
    /// Delegates to <see cref="BpRenderer.Render"/>.
    /// </summary>
    public Blueprint Project(IrWorkflow ir) => _renderer.Render(ir);

    /// <summary>
    /// Folds a stream of BP canvas edits back into an <see cref="IrDiff"/> against
    /// the baseline IR. Delegates to <see cref="BpEditTranslator.Translate"/>, which
    /// resolves BP names via the registry (NOT a hardcoded dictionary). The caller
    /// applies the resulting diff via <see cref="IrDiffApply"/>.
    /// </summary>
    public IrDiff Diff(IrWorkflow baseline, IReadOnlyList<BpEditAction> delta)
    {
        if (_registry is null)
            throw new InvalidOperationException(
                "BpGraphLens.Diff requires a BuiltinFunctionRegistry (BP-name → IR-name " +
                "resolution). Construct the lens with a registry, or use BpEditTranslator directly.");
        var translator = new BpEditTranslator(_registry);
        return translator.Translate(baseline, delta);
    }

    /// <summary>
    /// Convenience: fold a single BP edit into an <see cref="IrDiff"/>. Requires
    /// a registry (see <see cref="Diff(IrWorkflow, IReadOnlyList{BpEditAction})"/>).
    /// </summary>
    public IrDiff Diff(IrWorkflow baseline, BpEditAction action) => Diff(baseline, new[] { action });
}
