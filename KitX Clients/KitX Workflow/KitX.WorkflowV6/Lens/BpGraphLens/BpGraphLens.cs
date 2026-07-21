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
// Exec-edge edit. The implementation of that check is part of the implementation plan.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// BP graph ↔ structured-IR lens. Methods are placeholders pending the implementation
/// plan; signatures match the v5 contract so DI wiring works from day one.
/// </summary>
public sealed class BpGraphLens : ILens<Blueprint, IReadOnlyList<BpEditAction>>
{
    private readonly BuiltinFunctionRegistry _registry;

    public BpGraphLens(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Renders the structured IR as a Blueprint graph.</summary>
    public Blueprint Project(Workflow ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        var renderer = new BpRenderer(_registry);
        return renderer.Render(ir);
    }

    /// <summary>
    /// Folds a stream of BP edits back into the IR as a WorkflowDiff. Edit-time
    /// structured-reduction rejection (§7.2) happens inside this entry.
    /// </summary>
    public WorkflowDiff Diff(Workflow baseline, IReadOnlyList<BpEditAction> delta) =>
        throw new NotImplementedException("BpGraphLens.Diff: v6 structured-reduction check not implemented.");
}

/// <summary>
/// A description of a BP node to render: title, kind hint, port layout. Built by
/// <see cref="IBpRenderHandler"/>; consumed by <see cref="BpGraphLens"/>.
/// Mirrors <c>KitX.WorkflowIR.Builtin.BpNodeTemplate</c>.
/// </summary>
public sealed record BpNodeTemplate
{
    public required string Title { get; init; }
    public required FunctionKind Kind { get; init; }
    public IReadOnlyList<PortSpec> Inputs { get; init; } = [];
    public IReadOnlyList<PortSpec> Outputs { get; init; } = [];
}

/// <summary>
/// Information about a BP node being reverse-translated to IR: its canvas title,
/// its argument values (port → string), and its control-flow targets. Built by the
/// BP-edit translator; consumed by <see cref="IBpReverseHandler.BuildFromBp"/>.
/// Mirrors <c>KitX.WorkflowIR.Builtin.BpNodeInfo</c>, re-typed for the v6 lexical path.
/// </summary>
public sealed record BpNodeInfo
{
    public required string Title { get; init; }
    public IReadOnlyDictionary<string, string> Arguments { get; init; } = new Dictionary<string, string>();
    /// <summary>Outgoing control-flow targets (pin name → lexical path of the target body).</summary>
    public IReadOnlyList<(string PinName, string TargetPath)> Arms { get; init; } = [];
}
