namespace KitX.WorkflowV6.Lens.BpGraphLens;

// ─────────────────────────────────────────────────────────────────────────────
// ScopeRegion — a flattened sub-scope region for background-frame rendering.
//
// The v6 Blueprint is a flat node list (no nesting containers). Sub-scopes (if
// body / forEach body / while body / switch arms) are expressed purely by exec
// edges. The Dashboard frontend renders these as *decorative* background frames
// (see KScript-Blueprint-Correspondence.md §3.6) — visual grouping only, never
// nested containers.
//
// ScopeRegion is the bridge: ScopeAnalyzer walks the exec topology and emits one
// ScopeRegion per control-flow sub-scope, carrying the contained node IDs and a
// bounding box (computed from the nodes' canvas coordinates). The frontend reads
// this list to paint background frames whose colour cycles by nesting depth.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A flattened sub-scope region discovered by walking the Blueprint's exec topology.
/// Each control-flow node (Branch/Each/While/Switch) produces one ScopeRegion per
/// sub-scope output pin (True/False/Body/arms/Default). The frontend renders these
/// as decorative background frames.
/// </summary>
public sealed record ScopeRegion
{
    /// <summary>Stable identifier: <c>{ownerNodeId}:{pinName}</c>.</summary>
    public required string ScopeId { get; init; }

    /// <summary>The control-flow node that owns this sub-scope.</summary>
    public required string OwnerNodeId { get; init; }

    /// <summary>The owner's function name (Branch / Each / While / Switch).</summary>
    public required string OwnerFunctionName { get; init; }

    /// <summary>
    /// Human-readable sub-scope label: "Then", "Else", "Body", "Arm:43", "Default".
    /// </summary>
    public required string ScopeKind { get; init; }

    /// <summary>Nesting depth (0 = direct child of top-level). Drives colour cycling.</summary>
    public required int Depth { get; init; }

    /// <summary>All node IDs contained within this sub-scope (excluding the owner).</summary>
    public required IReadOnlyList<string> NodeIds { get; init; }

    /// <summary>Bounding-box X (canvas-space, from contained nodes).</summary>
    public required double X { get; init; }

    /// <summary>Bounding-box Y (canvas-space, from contained nodes).</summary>
    public required double Y { get; init; }

    /// <summary>Bounding-box width.</summary>
    public required double Width { get; init; }

    /// <summary>Bounding-box height.</summary>
    public required double Height { get; init; }
}
