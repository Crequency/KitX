namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// LayoutService — recursive subgraph layout engine for the v6 Blueprint.
//
// Ported from v5.1 KitX.WorkflowIR.LayoutService with the following v6 adaptations:
//
//   • No BlockNode: v6 has no block-container nodes. All statement nodes live
//     in the flat Blueprint.Nodes list, connected by Exec/Data edges. The
//     PlaceInnerNodes pass is eliminated entirely.
//
//   • ExecTail model: v6's control flow (if/forEach/while/switch) is modelled
//     as BuiltinFunctionNode with named Exec output pins (True/False, Body/End,
//     0..N/Default). BuildExecAdjacencyMap picks these up naturally — no
//     special-casing needed. Each/While's Body/End are treated as a 2-way Fork.
//
//   • Literal→DefaultValue: v6 sets pin.DefaultValue for literals instead of
//     creating ConstNode + data edges. This dramatically reduces the number of
//     ConstNodes; PlaceDataNodes still handles any that remain.
//
//   • Variable definition/use split: v6 creates separate VariableNode instances
//     for definitions (const/var declarations) and use sites (pipeline references).
//     Definition nodes have no Exec edges and fall through to PlaceDataNodes.
//
// The region-tree algorithm (LinearRegion/ForkRegion, smart row wrapping,
// symmetric fork branch arrangement) is preserved from v5.1 — it operates on
// the Blueprint.Connections exec graph which is structurally identical.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Recursive subgraph layout engine. Builds a tree of LayoutRegions
/// (Linear / Fork) from the exec chain, then measures and arranges each
/// region with vertical branch separation and smart line wrapping.
/// </summary>
public sealed class LayoutService : ILayoutService
{
    // ── Layout constants ──
    private const double HSpacing = 220;
    private const double VSpacing = 130;
    private const double ForkVGap = 100;
    private const double ForkHGap = 300;
    private const double MaxRowWidth = 660;
    private const double XOffset = 50;
    private const double YOffset = 50;
    private const double NodeWidth = 200;
    private const double NodeHeight = 100;

    // ── Data node sidebar ──
    private const double DataSidebarX = -350;
    private const double DataNodeVSpacing = 150;

    /// <inheritdoc />
    public void Layout(Blueprint blueprint)
    {
        if (blueprint.Nodes.Count == 0) return;

        // Pre-processing: ensure all nodes have non-zero dimensions so that
        // ForkRegion.Arrange can compute branch offsets correctly.
        EnsureDefaultSizes(blueprint);

        // Phase 1: Build exec adjacency map (Execution-pin connections only).
        var execMap = BuildExecAdjacencyMap(blueprint);

        // Phase 2: Find the single EntryNode (v6 has exactly one per workflow).
        var entry = blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
        if (entry == null) return;

        // Phase 3: Build region tree starting from entry.
        var visited = new HashSet<string>();
        var placed = new HashSet<string>();
        var rootRegion = BuildRegionTree(entry.Id, execMap, blueprint, visited, placed);

        // Phase 4: Measure and arrange.
        if (rootRegion != null)
        {
            rootRegion.Measure();
            rootRegion.Arrange(XOffset, YOffset, blueprint);
        }

        // Phase 5: Place data-only nodes (Const, Variable definitions) in sidebar.
        PlaceDataNodes(blueprint, placed);
    }

    /// <summary>
    /// Sets default Width/Height on nodes that have zero dimensions, so layout
    /// calculations produce correct spacing.
    /// </summary>
    private static void EnsureDefaultSizes(Blueprint blueprint)
    {
        foreach (var node in blueprint.Nodes)
        {
            if (node.Width <= 0) node.Width = NodeWidth;
            if (node.Height <= 0) node.Height = NodeHeight;
        }
    }

    /// <summary>
    /// Builds nodeId → [(pinName, targetNodeId)] mapping for exec-type connections
    /// only. Iterates by node OutputPins order (visual top-to-bottom) to ensure
    /// branch direction assignment matches physical pin layout.
    /// </summary>
    private static Dictionary<string, List<(string PinName, string TargetId)>> BuildExecAdjacencyMap(
        Blueprint blueprint)
    {
        var map = new Dictionary<string, List<(string, string)>>();

        foreach (var node in blueprint.Nodes)
        {
            var execPins = node.OutputPins.Where(p => p.Type == PinType.Execution).ToList();
            if (execPins.Count == 0) continue;

            var targets = new List<(string PinName, string TargetId)>();
            foreach (var pin in execPins)
            {
                var conn = blueprint.Connections.FirstOrDefault(c =>
                    c.SourceNodeId == node.Id && c.SourcePinId == pin.Id);
                if (conn != null)
                    targets.Add((pin.Name, conn.TargetNodeId));
            }

            if (targets.Count > 0)
                map[node.Id] = targets;
        }

        return map;
    }

    /// <summary>
    /// Recursively builds a tree of LayoutRegions from the exec chain.
    /// Fork nodes (Branch, Each, While, Switch) are kept in the parent
    /// LinearRegion so they appear at the end of the linear chain, not at
    /// the base X. The ForkRegion (branches only) becomes its Child.
    /// </summary>
    private static LayoutRegion? BuildRegionTree(
        string nodeId,
        Dictionary<string, List<(string PinName, string TargetId)>> execMap,
        Blueprint blueprint,
        HashSet<string> visited,
        HashSet<string> placed)
    {
        if (visited.Contains(nodeId)) return null;
        visited.Add(nodeId);

        if (!execMap.TryGetValue(nodeId, out var targets) || targets.Count == 0)
        {
            placed.Add(nodeId);
            return new LinearRegion(nodeId);
        }

        if (targets.Count == 1)
        {
            var childId = targets[0].TargetId;
            var linear = new LinearRegion();
            linear.NodeIds.Add(nodeId);
            placed.Add(nodeId);

            if (!visited.Contains(childId))
            {
                var childRegion = BuildRegionTree(childId, execMap, blueprint, visited, placed);
                if (childRegion is LinearRegion childLinear)
                {
                    linear.NodeIds.AddRange(childLinear.NodeIds);
                    if (childLinear.Child != null)
                        linear.Child = childLinear.Child;
                }
                else if (childRegion != null)
                {
                    linear.Child = childRegion;
                }
            }

            return linear;
        }

        // Fork: 2+ exec outputs (Branch True/False, Each/While Body/End, Switch 0..N/Default).
        placed.Add(nodeId);

        var branches = new List<LayoutRegion?>();
        foreach (var target in targets)
        {
            branches.Add(BuildRegionTree(target.TargetId, execMap, blueprint, visited, placed));
        }

        var forkLinear = new LinearRegion(nodeId);
        forkLinear.Child = new ForkRegion(nodeId, branches);
        return forkLinear;
    }

    /// <summary>
    /// Places data-only nodes (Const, Variable definitions, variable use sites)
    /// in a left sidebar column. These are nodes not reachable from the exec
    /// chain — they participate only in data flow.
    /// </summary>
    private void PlaceDataNodes(Blueprint blueprint, HashSet<string> placed)
    {
        var dataNodes = blueprint.Nodes.Where(n => !placed.Contains(n.Id)).ToList();
        if (dataNodes.Count == 0) return;

        // All unplaced nodes go to the left sidebar, stacked vertically.
        // In v6 this includes: VariableNode (definitions + use sites), ConstNode.
        for (int i = 0; i < dataNodes.Count; i++)
        {
            dataNodes[i].X = DataSidebarX;
            dataNodes[i].Y = YOffset + i * DataNodeVSpacing;
            placed.Add(dataNodes[i].Id);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout Region Types
    // ─────────────────────────────────────────────────────────────────────────

    private abstract class LayoutRegion
    {
        public double MeasuredWidth { get; protected set; }
        public double MeasuredHeight { get; protected set; }
        public abstract void Measure(double availableWidth = MaxRowWidth);
        public abstract void Arrange(double x, double y, Blueprint bp);
    }

    /// <summary>
    /// Linear chain of nodes, left-to-right, with smart wrapping at availableWidth.
    /// </summary>
    private class LinearRegion : LayoutRegion
    {
        public List<string> NodeIds { get; } = [];
        public LayoutRegion? Child;

        private List<List<string>> _rows = [];

        public LinearRegion() { }

        public LinearRegion(string singleNodeId)
        {
            NodeIds.Add(singleNodeId);
        }

        public override void Measure(double availableWidth = MaxRowWidth)
        {
            _rows.Clear();
            if (NodeIds.Count == 0 && Child == null)
            {
                MeasuredWidth = 0;
                MeasuredHeight = 0;
                return;
            }

            var currentRow = new List<string>();
            double rowWidth = 0;

            foreach (var nodeId in NodeIds)
            {
                var nodeWidth = rowWidth == 0 ? NodeWidth : HSpacing + NodeWidth;
                if (rowWidth + nodeWidth > availableWidth && currentRow.Count > 0)
                {
                    _rows.Add(currentRow);
                    currentRow = [];
                    rowWidth = 0;
                    nodeWidth = NodeWidth;
                }
                currentRow.Add(nodeId);
                rowWidth += nodeWidth;
            }

            if (currentRow.Count > 0)
                _rows.Add(currentRow);

            MeasuredWidth = _rows.Count > 0
                ? _rows.Max(r => (r.Count - 1) * HSpacing + NodeWidth)
                : 0;
            MeasuredHeight = _rows.Count > 0
                ? (_rows.Count - 1) * VSpacing + NodeHeight
                : 0;

            if (Child != null)
            {
                Child.Measure(availableWidth);
                MeasuredWidth = Math.Max(MeasuredWidth, Child.MeasuredWidth);
                MeasuredHeight += Child.MeasuredHeight > 0 ? VSpacing + Child.MeasuredHeight : 0;
            }
        }

        public override void Arrange(double x, double y, Blueprint bp)
        {
            double currentY = y;

            foreach (var row in _rows)
            {
                double currentX = x;
                foreach (var nodeId in row)
                {
                    var node = bp.Nodes.FirstOrDefault(n => n.Id == nodeId);
                    if (node != null)
                    {
                        node.X = currentX;
                        node.Y = currentY;
                    }
                    currentX += HSpacing;
                }
                currentY += VSpacing;
            }

            if (Child != null)
            {
                double childX = x;
                double childY = _rows.Count > 0 ? currentY : y;
                Child.Arrange(childX, childY, bp);
            }
        }
    }

    /// <summary>
    /// Fork region: arranges N sub-branches vertically around a fork node
    /// that has already been placed by the parent LinearRegion.
    /// Does NOT place the fork node itself — only arranges branches
    /// relative to the fork node's actual position in the blueprint.
    /// </summary>
    private class ForkRegion : LayoutRegion
    {
        public string ForkNodeId;
        public List<LayoutRegion?> Branches;

        private double _upperBranchHeight;
        private double _middleBranchHeight;

        public ForkRegion(string forkNodeId, List<LayoutRegion?> branches)
        {
            ForkNodeId = forkNodeId;
            Branches = branches;
        }

        /// <summary>
        /// Determines layout direction for a branch by its index.
        /// -1 = upper-right, 0 = straight-right (same Y as fork), 1 = lower-right.
        /// </summary>
        private static int GetBranchDirection(int index, int total)
        {
            double mid = (total + 1) / 2.0;
            if (index + 1 < mid) return -1;
            if (index + 1 == mid) return 0;
            return 1;
        }

        public override void Measure(double availableWidth = MaxRowWidth)
        {
            foreach (var branch in Branches)
                branch?.Measure(availableWidth);

            double maxBranchWidth = Branches
                .Where(b => b != null)
                .Select(b => b!.MeasuredWidth)
                .DefaultIfEmpty(0)
                .Max();

            MeasuredWidth = ForkHGap + maxBranchWidth;

            _upperBranchHeight = 0;
            _middleBranchHeight = 0;
            double lowerBranchHeight = 0;

            for (int i = 0; i < Branches.Count; i++)
            {
                var branch = Branches[i];
                if (branch == null || branch.MeasuredHeight <= 0) continue;

                int dir = GetBranchDirection(i, Branches.Count);
                switch (dir)
                {
                    case -1:
                        if (_upperBranchHeight > 0) _upperBranchHeight += VSpacing;
                        _upperBranchHeight += branch.MeasuredHeight;
                        break;
                    case 0:
                        _middleBranchHeight = branch.MeasuredHeight;
                        break;
                    case 1:
                        if (lowerBranchHeight > 0) lowerBranchHeight += VSpacing;
                        lowerBranchHeight += branch.MeasuredHeight;
                        break;
                }
            }

            MeasuredHeight = _upperBranchHeight
                + (_upperBranchHeight > 0 ? ForkVGap : 0)
                + _middleBranchHeight
                + (lowerBranchHeight > 0 ? ForkVGap : 0)
                + lowerBranchHeight;
        }

        public override void Arrange(double x, double y, Blueprint bp)
        {
            var forkNode = bp.Nodes.FirstOrDefault(n => n.Id == ForkNodeId);
            if (forkNode == null) return;

            double branchBaseX = forkNode.X + forkNode.Width + ForkHGap;

            double upperStartY = forkNode.Y - ForkVGap;

            double maxUpperBottom = upperStartY;
            for (int i = 0; i < Branches.Count; i++)
            {
                if (GetBranchDirection(i, Branches.Count) == -1)
                {
                    var b = Branches[i];
                    if (b != null && b.MeasuredHeight > 0)
                        maxUpperBottom = Math.Max(maxUpperBottom, upperStartY + b.MeasuredHeight);
                }
            }

            double currentUpperY = upperStartY;
            double currentLowerY = Math.Max(
                forkNode.Y + forkNode.Height + ForkVGap,
                maxUpperBottom + ForkVGap);

            for (int i = 0; i < Branches.Count; i++)
            {
                var branch = Branches[i];
                if (branch == null || branch.MeasuredHeight <= 0) continue;

                int dir = GetBranchDirection(i, Branches.Count);
                switch (dir)
                {
                    case -1:
                        branch.Arrange(branchBaseX, currentUpperY, bp);
                        currentUpperY += branch.MeasuredHeight + VSpacing;
                        break;
                    case 0:
                        branch.Arrange(branchBaseX, forkNode.Y, bp);
                        break;
                    case 1:
                        branch.Arrange(branchBaseX, currentLowerY, bp);
                        currentLowerY += branch.MeasuredHeight + VSpacing;
                        break;
                }
            }
        }
    }
}
