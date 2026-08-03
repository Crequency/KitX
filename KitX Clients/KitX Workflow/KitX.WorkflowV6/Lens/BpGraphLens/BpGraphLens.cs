namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// BpGraphLens — BP graph ↔ structured IR (v6).
//
// Inherited contract from KitX.WorkflowIR.Lens.BpGraphLens.BpGraphLens: this lens is
// the bidirectional bridge between the structured IR and the on-canvas Blueprint graph.
// Project renders the IR as a Blueprint; Diff folds a stream of BP edits back into the
// IR as a WorkflowDiff.
//
// The v6 BP side enforces a *structured-graph* constraint (discussion notes §7): the
// canvas graph must reduce to a structured tree. Non-structural back edges are rejected
// at edit time. Loops are expressed by control-flow nodes (ForEach / While) whose Body
// output pin connects to a sub-graph that implicitly re-enters the loop node; the
// editor's connection validator invokes the structured-reduction check (§7.2) on every
// Exec-edge edit.
//
// Status:
//   • Project — fully implemented (BpRenderer + LayoutService)
//   • Reverse — fully implemented (BpReverseTranslator, 13 round-trip tests)
//   • Diff — currently a stub (BpEditTranslator produces placeholder StatementChanges
//     with NewValue=null). Full V6-native implementation is deferred to the project's
//     P2 milestone (dual-pane live highlight). See
//     Package/Archive/Docs/V6-BpEditAction-Future-Design-ADR.md for the future design.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// BP graph ↔ structured-IR lens. Project and Reverse are fully implemented;
/// Diff currently produces placeholder changes pending the P2 milestone redesign.
/// </summary>
public sealed class BpGraphLens : ILens<Blueprint, IReadOnlyList<BpEditAction>>
{
    private readonly BuiltinFunctionRegistry _registry;
    private readonly IScopeAnalyzer _scopeAnalyzer;

    public BpGraphLens(BuiltinFunctionRegistry registry)
        : this(registry, new ScopeAnalyzer())
    {
    }

    /// <summary>Internal constructor allowing a custom scope analyzer (testing/DI).</summary>
    internal BpGraphLens(BuiltinFunctionRegistry registry, IScopeAnalyzer scopeAnalyzer)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _scopeAnalyzer = scopeAnalyzer ?? throw new ArgumentNullException(nameof(scopeAnalyzer));
    }

    /// <summary>Renders the structured IR as a Blueprint graph.</summary>
    public Blueprint Project(Workflow ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var renderer = new BpRenderer(_registry);
        return renderer.Render(ir);
    }

    /// <summary>
    /// Validates the blueprint and returns a structured violation (with node IDs for
    /// frontend highlighting) on failure, or null on success. Frontend BP editor calls
    /// this during connection hover-preview and on commit to enforce strong constraints.
    /// </summary>
    public ConstraintViolation? ValidateDetailed(Blueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        return StructuralReducer.CheckDetailed(blueprint);
    }

    /// <summary>
    /// Analyzes the Blueprint's exec topology and returns sub-scope regions for
    /// decorative background-frame rendering. Coordinates must already be assigned
    /// (call after <see cref="Project"/>, which runs LayoutService internally).
    /// </summary>
    public IReadOnlyList<ScopeRegion> AnalyzeScopes(Blueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        return _scopeAnalyzer.Analyze(blueprint);
    }

    /// <summary>
    /// Reconstructs a structured IR from a Blueprint graph (the reverse of
    /// <see cref="Project"/>). Enables the BP → IR → BP round-trip: Project then
    /// Reverse yields an IR structurally equal to the original.
    /// </summary>
    /// <param name="bp">The blueprint to reverse.</param>
    /// <param name="helpers">
    /// Optional user helper functions. The BP graph does NOT carry helper metadata
    /// (helper bodies live only in the IR), so the reversed IR would otherwise lose
    /// them — the Dashboard BP-mode Run/Save/Debug paths pass them through here
    /// (previously re-attached frontend-side via a ReverseWithHelpers wrapper).
    /// </param>
    public Workflow Reverse(Blueprint bp, IReadOnlyList<HelperFunction>? helpers = null)
    {
        ArgumentNullException.ThrowIfNull(bp);
        var translator = new BpReverseTranslator(_registry);
        var ir = translator.Reverse(bp);
        if (helpers is { Count: > 0 })
            ir = ir with { HelperFunctions = [.. helpers] };
        return ir;
    }

    /// <summary>
    /// Folds a stream of BP edits back into the IR as a WorkflowDiff. Edit-time
    /// structured-reduction rejection (§7.2) happens inside this entry.
    /// </summary>
    public WorkflowDiff Diff(Workflow baseline, IReadOnlyList<BpEditAction> delta)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(delta);

        // Project the baseline IR to a Blueprint so the translator can validate structure.
        var bp = new BpRenderer(_registry).Render(baseline);
        var translator = new BpEditTranslator(_registry);
        var (diff, error) = translator.Translate(bp, delta);
        if (error is not null)
        {
            // The structured-reduction check rejected the edit; surface the error.
            throw new InvalidOperationException(error);
        }
        return diff!;
    }
}
