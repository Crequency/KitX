namespace KitX.WorkflowV6.Ir;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// DetachedGraph — a BP-side "privileged" exec sub-graph that has no KS
// counterpart (workflow KScript-Blueprint-Correspondence §5.5 / frontend plan
// §八-附 设计 C).
//
// When the user disconnects an exec edge (or builds a sub-graph without ever
// wiring it into the Entry-reachable exec chain), the reverse translator must
// NOT drop the orphaned nodes — that would silently destroy BP-side work on a
// KS→BP round-trip. Instead the orphaned component is snapshotted here:
//
//   • Nodes       — full Contract snapshots (IDs, coordinates, pins, comments)
//   • Connections — edges INSIDE the component (cross-component data edges are
//                   rejected at the frontend connect layer; exec edges cannot
//                   cross components by construction)
//
// The snapshot is preserved verbatim by Project (BpRenderer re-emits the nodes
// and connections at their stored coordinates) and by WorkflowSerializer (the
// .kcs IrData round-trip). It is invisible to KS: KsTextLens.Project never
// reads this field, and the execution backend never compiles it.
//
// "Privilege" semantics: the detached graph exists only in IR + BP. A user can
// re-attach it at any time by drawing an exec edge from the main chain into one
// of its nodes — after which the component becomes reachable again and Reverse
// folds it back into the statement body.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A snapshot of an exec-unreachable (detached) Blueprint sub-graph. Carried in
/// <see cref="Workflow.DetachedGraphs"/>; preserved across Reverse/Project and
/// file round-trips; never rendered to KS text.
/// </summary>
public sealed record DetachedGraph
{
    /// <summary>Component id (stable identifier for the snapshot).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Detached nodes (Contract snapshots — IDs/coordinates/pins/comments preserved).</summary>
    public ImmutableArray<BlueprintNode> Nodes { get; init; } = [];

    /// <summary>Edges whose BOTH endpoints belong to <see cref="Nodes"/>.</summary>
    public ImmutableArray<BlueprintConnection> Connections { get; init; } = [];
}
