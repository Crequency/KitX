namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// ScopeAnalyzer — walks the Blueprint's exec topology to discover sub-scope regions.
//
// The analysis mirrors the structured-reduction walk (StructuralReducer.WalkStructured)
// and the reverse translator's WalkExecChain, but its sole purpose is to collect
// *which nodes belong to which sub-scope*. It does not validate or build IR.
//
// The traversal itself is shared: both this analyzer and StructuralReducer drive the
// ExecGraphWalker skeleton (sub-scope recursion + End-pin continuation), differing only
// in the per-node hooks. See ExecGraphWalker for the traversal contract.
//
// Algorithm:
//   1. Index exec edges via GraphIndex (loose exec index — source-pin-only filter,
//      matching the original IndexExecOut which never inspected the target pin).
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
        var graph = new GraphIndex(blueprint);
        // Entry or PluginTrigger (trigger entry node replaces Entry when TriggerType=PluginEvent).
        var entry = blueprint.Nodes.FirstOrDefault(n => n is EntryNode or PluginTriggerNode);
        if (entry is null) return [];

        var regions = new List<ScopeRegion>();

        // Top-level walk: currentScope is null (top-level nodes are not framed).
        // Scope paths follow the ExecGraphWalker convention: root = NodePath.Top.
        var visitor = new ScopeCollectVisitor(regions);
        visitor.Walk(graph, entry.Id, BpPinNames.Exec, NodePath.Top);

        // Compute bounding boxes from node coordinates. A parent region's frame must
        // ENCLOSE all its nested sub-regions, so child frames are merged recursively
        // (a child is a region whose owner control-flow node sits in this region's
        // node set; grandchildren come along transitively through the child's frame).
        var computed = new Dictionary<string, Box>();
        var boxes = new Dictionary<string, Box>(regions.Count);
        foreach (var r in regions)
            boxes[r.ScopeId] = ComputeBox(r, regions, byId, computed);
        return [.. regions.Select(r => r with { X = boxes[r.ScopeId].X, Y = boxes[r.ScopeId].Y, Width = boxes[r.ScopeId].Width, Height = boxes[r.ScopeId].Height })];
    }

    private readonly record struct Box(double X, double Y, double Width, double Height);

    private static Box ComputeBox(
        ScopeRegion region,
        IReadOnlyList<ScopeRegion> all,
        Dictionary<string, BlueprintNode> byId,
        Dictionary<string, Box> memo)
    {
        if (memo.TryGetValue(region.ScopeId, out var cached))
            return cached;

        if (region.NodeIds.Count == 0)
        {
            // Empty body: place a minimal frame at the owner node's position.
            if (byId.TryGetValue(region.OwnerNodeId, out var emptyOwner))
            {
                var empty = new Box(
                    emptyOwner.X + emptyOwner.Width + FramePadding,
                    emptyOwner.Y,
                    80, 50);
                memo[region.ScopeId] = empty;
                return empty;
            }
            memo[region.ScopeId] = default;
            return default;
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
        if (minX == double.MaxValue)
        {
            memo[region.ScopeId] = default;
            return default;
        }

        // Merge every nested sub-region's frame (child frames already include their own
        // descendants). A child's owner node belongs to this region's node set.
        foreach (var child in all)
        {
            if (child.ScopeId == region.ScopeId) continue;
            if (!region.NodeIds.Contains(child.OwnerNodeId)) continue;
            var cb = ComputeBox(child, all, byId, memo);
            minX = Math.Min(minX, cb.X);
            minY = Math.Min(minY, cb.Y);
            maxX = Math.Max(maxX, cb.X + cb.Width);
            maxY = Math.Max(maxY, cb.Y + cb.Height);
        }

        var box = new Box(
            minX - FramePadding,
            minY - FramePadding,
            (maxX - minX) + FramePadding * 2,
            (maxY - minY) + FramePadding * 2);
        memo[region.ScopeId] = box;
        return box;
    }

    // ── ExecGraphWalker visitor: sub-scope region collection ──
    //
    // Semantics preserved from the original WalkChain:
    //   • Global visited set — repeated visits are skipped silently.
    //   • Control-flow node itself belongs to the CURRENT scope; each sub-scope pin
    //     (except End) opens a fresh child scope-set + ScopeRegion placeholder whose
    //     NodeIds are back-filled on exit (nested regions append AFTER the placeholder,
    //     so the recorded index is stable — the regions[^1] clobbering regression).
    //   • Region Depth = nesting depth (0 = direct child of top-level), derived from
    //     the scope path: root NodePath.Top = 1 segment.

    private sealed class ScopeCollectVisitor : ExecGraphWalker
    {
        private readonly List<ScopeRegion> _regions;
        private readonly HashSet<string> _globalVisited = new();
        private readonly Stack<HashSet<string>?> _scopes = new();
        private readonly Stack<int> _regionIndexes = new();

        public ScopeCollectVisitor(List<ScopeRegion> regions)
        {
            _regions = regions;
            _scopes.Push(null);  // top level: no frame — nodes are not collected.
        }

        protected override VisitDecision OnNode(BlueprintNode node, string scopePath)
        {
            if (!_globalVisited.Add(node.Id)) return VisitDecision.Skip;
            _scopes.Peek()?.Add(node.Id);
            return VisitDecision.Visit;
        }

        protected override void OnEnterSubScope(BuiltinFunctionNode fn, string pinName, string childScopePath)
        {
            var childScope = new HashSet<string>();
            _scopes.Push(childScope);

            // Record the index BEFORE recursing: nested scopes append their own regions
            // afterwards, so regions[^1] would not reference this placeholder.
            var regionIndex = _regions.Count;
            _regions.Add(new ScopeRegion
            {
                ScopeId = $"{fn.Id}:{pinName}",
                OwnerNodeId = fn.Id,
                OwnerFunctionName = fn.FunctionName,
                ScopeKind = DeriveScopeKind(fn.FunctionName, pinName),
                Depth = NestedDepth(childScopePath),
                NodeIds = [],  // filled on exit, below
                X = 0, Y = 0, Width = 0, Height = 0,  // computed later
            });
            _regionIndexes.Push(regionIndex);
        }

        protected override void OnExitSubScope(BuiltinFunctionNode fn, string pinName, string childScopePath)
        {
            var childScope = _scopes.Pop()!;  // walker guarantees Push/Pop pairing
            var regionIndex = _regionIndexes.Pop();
            // Replace THIS placeholder region's NodeIds with the collected set.
            var placeholder = _regions[regionIndex];
            _regions[regionIndex] = placeholder with { NodeIds = [.. childScope] };
        }

        /// <summary>
        /// Nesting depth of a scope path (0 = direct child of top level). The root walk
        /// seeds NodePath.Top ("/top", 1 segment) and every sub-scope appends one
        /// segment, so depth = segment count - 1 = "/" count - 1... the child scope of
        /// a top-level control-flow node ("/top/then") has 2 segments → depth 0.
        /// </summary>
        private static int NestedDepth(string scopePath)
        {
            int slashes = 0;
            foreach (var c in scopePath)
                if (c == '/') slashes++;
            return slashes - 2;
        }

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
    }
}
