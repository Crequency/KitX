namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// ScopeAnalyzer — walks the Blueprint's exec topology to discover sub-scope regions.
//
// The analysis mirrors the structured-reduction walk (StructuralReducer.WalkStructured)
// and the reverse translator's WalkExecChain, but its sole purpose is to collect
// *which nodes belong to which sub-scope*. It does not validate or build IR.
//
// Algorithm:
//   1. Index exec edges: (sourceNodeId, pinName) → target nodes.
//   2. Walk from EntryNode.Exec. Maintain a "current scope" node-set (null at top level).
//   3. At each node:
//        • Control-flow node (Branch/Each/While/Switch): belongs to current scope.
//          For each sub-scope output pin (True/False/Body/arms/Default) create a fresh
//          child scope-set + ScopeRegion and recurse at depth+1. Then continue from
//          the End pin into the current scope.
//        • Terminator (break/continue): belongs to current scope; chain ends.
//        • Ordinary node: belongs to current scope; continue from Exec out.
//   4. After walking, compute each region's bounding box from its nodes' coordinates.
//
// This is a full O(V+E) re-computation, called after every connectivity-changing BP
// edit. Workflows are far smaller than the BF-compiler demo, so latency is negligible.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Default <see cref="IScopeAnalyzer"/> implementation. Walks the exec topology to
/// produce one <see cref="ScopeRegion"/> per control-flow sub-scope.
/// </summary>
internal sealed class ScopeAnalyzer : IScopeAnalyzer
{
    private const double FramePadding = 24.0;

    /// <inheritdoc/>
    public IReadOnlyList<ScopeRegion> Analyze(Blueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        if (blueprint.Nodes.Count == 0) return [];

        var byId = blueprint.Nodes.ToDictionary(n => n.Id);
        var execOut = IndexExecOut(blueprint, byId);
        var entry = blueprint.Nodes.OfType<EntryNode>().FirstOrDefault();
        if (entry is null) return [];

        var regions = new List<ScopeRegion>();
        var globalVisited = new HashSet<string>();

        // Top-level walk: currentScope is null (top-level nodes are not framed).
        WalkChain(entry.Id, BpPinNames.Exec, depth: 0,
            currentScope: null, byId, execOut, regions, globalVisited);

        // Compute bounding boxes from node coordinates.
        return [.. regions.Select(r => WithBoundingBox(r, byId))];
    }

    // ── Exec-edge indexing ──

    private static Dictionary<(string NodeId, string PinName), List<string>> IndexExecOut(
        Blueprint bp, Dictionary<string, BlueprintNode> byId)
    {
        var execOut = new Dictionary<(string, string), List<string>>();
        foreach (var conn in bp.Connections)
        {
            if (!byId.TryGetValue(conn.SourceNodeId, out var src)) continue;
            if (!byId.TryGetValue(conn.TargetNodeId, out _)) continue;
            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type != PinType.Execution) continue;
            var key = (conn.SourceNodeId, srcPin.Name);
            if (!execOut.TryGetValue(key, out var list))
            {
                list = new List<string>();
                execOut[key] = list;
            }
            list.Add(conn.TargetNodeId);
        }
        return execOut;
    }

    // ── Recursive exec-chain walk ──

    private void WalkChain(
        string sourceId, string pinName, int depth,
        HashSet<string>? currentScope,
        Dictionary<string, BlueprintNode> byId,
        Dictionary<(string, string), List<string>> execOut,
        List<ScopeRegion> regions,
        HashSet<string> globalVisited)
    {
        if (!execOut.TryGetValue((sourceId, pinName), out var targets)) return;

        foreach (var targetId in targets)
        {
            if (!globalVisited.Add(targetId)) continue;
            if (!byId.TryGetValue(targetId, out var node)) continue;

            if (node is BuiltinFunctionNode fn && IsControlFlowName(fn.FunctionName))
            {
                // The control-flow node itself belongs to the current scope.
                currentScope?.Add(targetId);

                // Create a child ScopeRegion for each sub-scope output pin (except End).
                foreach (var subPin in fn.OutputPins)
                {
                    if (subPin.Name == BpPinNames.End) continue;
                    if (subPin.Type != PinType.Execution) continue;

                    var childScope = new HashSet<string>();
                    regions.Add(new ScopeRegion
                    {
                        ScopeId = $"{targetId}:{subPin.Name}",
                        OwnerNodeId = targetId,
                        OwnerFunctionName = fn.FunctionName,
                        ScopeKind = DeriveScopeKind(fn.FunctionName, subPin.Name),
                        Depth = depth,
                        NodeIds = [],  // filled below, then made readonly after walk
                        X = 0, Y = 0, Width = 0, Height = 0,  // computed later
                    });
                    // Recurse into the sub-scope with the child set as current scope.
                    WalkChain(targetId, subPin.Name, depth + 1,
                        childScope, byId, execOut, regions, globalVisited);
                    // Replace the placeholder region's NodeIds with the collected set.
                    var placeholder = regions[^1];
                    regions[^1] = placeholder with { NodeIds = [.. childScope] };
                }

                // Continue from the End pin into the current scope.
                WalkChain(targetId, BpPinNames.End, depth,
                    currentScope, byId, execOut, regions, globalVisited);
            }
            else if (IsTerminator(node))
            {
                currentScope?.Add(targetId);
                // Terminator has no exec-out; chain ends here.
            }
            else
            {
                currentScope?.Add(targetId);
                WalkChain(targetId, BpPinNames.Exec, depth,
                    currentScope, byId, execOut, regions, globalVisited);
            }
        }
    }

    // ── Helpers ──

    private static bool IsControlFlowName(string name)
        => name is "Branch" or "Each" or "While" or "Switch";

    private static bool IsTerminator(BlueprintNode node)
        => node is BuiltinFunctionNode fn
           && (fn.FunctionName == "break" || fn.FunctionName == "continue");

    /// <summary>
    /// Maps a control-flow function name + sub-scope pin name to a human-readable label.
    /// </summary>
    private static string DeriveScopeKind(string functionName, string pinName) => functionName switch
    {
        "Branch" => pinName == BpPinNames.True ? "Then"
                    : pinName == BpPinNames.False ? "Else"
                    : pinName,
        "Each" or "While" => pinName == BpPinNames.Body ? "Body" : pinName,
        "Switch" => pinName == BpPinNames.Default ? "Default"
                    : int.TryParse(pinName, out _) ? $"Arm:{pinName}"
                    : pinName,
        _ => pinName,
    };

    /// <summary>
    /// Computes the bounding box of a region from its contained nodes' coordinates.
    /// Returns the region unchanged if it has no nodes (empty body).
    /// </summary>
    private static ScopeRegion WithBoundingBox(ScopeRegion region, Dictionary<string, BlueprintNode> byId)
    {
        if (region.NodeIds.Count == 0)
        {
            // Empty body: place a minimal frame at the owner node's position.
            if (byId.TryGetValue(region.OwnerNodeId, out var owner))
            {
                return region with
                {
                    X = owner.X + owner.Width + FramePadding,
                    Y = owner.Y,
                    Width = 80,
                    Height = 50,
                };
            }
            return region;
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var nodeId in region.NodeIds)
        {
            if (!byId.TryGetValue(nodeId, out var n)) continue;
            minX = Math.Min(minX, n.X);
            minY = Math.Min(minY, n.Y);
            maxX = Math.Max(maxX, n.X + n.Width);
            maxY = Math.Max(maxY, n.Y + n.Height);
        }
        if (minX == double.MaxValue) return region;

        return region with
        {
            X = minX - FramePadding,
            Y = minY - FramePadding,
            Width = (maxX - minX) + FramePadding * 2,
            Height = (maxY - minY) + FramePadding * 2,
        };
    }
}
