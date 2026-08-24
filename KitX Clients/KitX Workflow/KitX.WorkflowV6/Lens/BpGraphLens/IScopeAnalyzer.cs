namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// IScopeAnalyzer — discovers sub-scope regions for background-frame rendering.
//
// The v6 Blueprint stores all nodes in a flat list; sub-scope membership (which
// nodes belong to an if-body, a forEach-body, etc.) is implied by the exec-edge
// topology. This interface decouples that analysis from both the layout engine
// (LayoutService assigns coordinates) and the reverse translator (which rebuilds
// IR statements). The frontend calls this after Project to obtain ScopeRegion[]
// for painting decorative background frames.
//
// Per the design decision (WorkflowV6-Dashboard-Frontend-Design.md): the analysis
// is a full O(V+E) re-computation on every BP edit that touches connectivity.
// Workflows are far smaller than the BF-compiler demo, so latency is negligible.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Analyzes a Blueprint's exec topology to produce sub-scope regions for
/// background-frame rendering. Pure: the blueprint is never mutated.
/// </summary>
public interface IScopeAnalyzer
{
    /// <summary>
    /// Walks the Blueprint's exec edges and returns one <see cref="ScopeRegion"/> per
    /// control-flow sub-scope (if-then, if-else, forEach-body, while-body, switch arms).
    /// Coordinates must already be assigned (call after LayoutService).
    /// </summary>
    IReadOnlyList<ScopeRegion> Analyze(Blueprint blueprint);
}
