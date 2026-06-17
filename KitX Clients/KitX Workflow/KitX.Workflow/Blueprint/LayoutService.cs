using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Workflow.Blueprint;

/// <summary>
/// Recursive subgraph layout engine.
/// Builds a tree of LayoutRegions (Linear / Fork) from the exec chain,
/// then measures and arranges each region with vertical branch separation
/// and smart line wrapping.
/// </summary>
public class LayoutService : ILayoutService
{
    // Layout constants
    private const double HSpacing = 220;
    private const double VSpacing = 130;
    private const double ForkVGap = 100;
    private const double ForkHGap = 300;
    private const double MaxRowWidth = 660;
    private const double XOffset = 50;
    private const double YOffset = 50;
    private const double NodeWidth = 200;
    private const double NodeHeight = 100;

    // Data node sidebar constants
    private const double DataSidebarX = -350;
    private const double ConstSidebarX = -350;
    private const double DataNodeVSpacing = 150;

    /// <inheritdoc />
    public void LayoutNodes(KitX.Core.Contract.Workflow.Blueprint blueprint)
    {
        if (blueprint.Nodes.Count == 0) return;

        Log.Debug("[Layout] === LayoutNodes called: {NodeCount} nodes, {ConnCount} connections ===",
            blueprint.Nodes.Count, blueprint.Connections.Count);

        // Phase 1: Build exec adjacency map
        var execMap = BuildExecAdjacencyMap(blueprint);

        // Phase 2: Find entry node
        var entry = blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
        if (entry == null)
        {
            Log.Warning("[Layout] No Entry node found, skipping layout");
            return;
        }

        // Phase 3: Build region tree starting from entry
        var visited = new HashSet<string>();
        var placed = new HashSet<string>();
        var rootRegion = BuildRegionTree(entry.Id, execMap, blueprint, visited, placed);

        // Phase 4: Measure and arrange
        if (rootRegion != null)
        {
            rootRegion.Measure();
            rootRegion.Arrange(XOffset, YOffset, blueprint);
        }

        // Phase 5: Place data nodes in sidebar
        PlaceDataNodes(blueprint, placed);

        // Phase 6: Log final positions
        foreach (var node in blueprint.Nodes)
        {
            Log.Debug("[Layout]   {Name,-35} ({Type,-6}) at ({X:F0}, {Y:F0})",
                node.Name, node.NodeType, node.X, node.Y);
        }
        Log.Debug("[Layout] === Layout complete: {Placed} nodes positioned ===", placed.Count);
    }

    /// <summary>
    /// Builds nodeId → [(pinName, targetNodeId)] mapping for exec-type connections only.
    /// Iterates by node OutputPins order (visual top-to-bottom) to ensure branch
    /// direction assignment matches physical pin layout.
    /// </summary>
    private Dictionary<string, List<(string PinName, string TargetId)>> BuildExecAdjacencyMap(
        KitX.Core.Contract.Workflow.Blueprint blueprint)
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

        Log.Debug("[Layout] Exec adjacency map: {Count} nodes with exec outputs", map.Count);
        return map;
    }

    /// <summary>
    /// Recursively builds a tree of LayoutRegions from the exec chain.
    /// Fork nodes (Branch, Loop, etc.) are kept in the parent LinearRegion
    /// so they appear at the end of the linear chain, not at the base X.
    /// </summary>
    private LayoutRegion? BuildRegionTree(
        string nodeId,
        Dictionary<string, List<(string PinName, string TargetId)>> execMap,
        KitX.Core.Contract.Workflow.Blueprint blueprint,
        HashSet<string> visited,
        HashSet<string> placed)
    {
        if (visited.Contains(nodeId)) return null;
        visited.Add(nodeId);

        if (!execMap.TryGetValue(nodeId, out var targets) || targets.Count == 0)
        {
            // Leaf node — single linear region
            placed.Add(nodeId);
            return new LinearRegion(nodeId);
        }

        if (targets.Count == 1)
        {
            // Single exec output — linear chain
            var childId = targets[0].TargetId;
            var linear = new LinearRegion();
            linear.NodeIds.Add(nodeId);
            placed.Add(nodeId);

            // Append child's chain if not yet visited
            if (!visited.Contains(childId))
            {
                var childRegion = BuildRegionTree(childId, execMap, blueprint, visited, placed);
                if (childRegion is LinearRegion childLinear)
                {
                    linear.NodeIds.AddRange(childLinear.NodeIds);
                    // Preserve the child's Child (e.g., a ForkRegion) when merging
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

        if (targets.Count >= 2)
        {
            // Fork: Branch (True/False), Loop (LoopBody/LoopEnd), or any N-way control flow.
            // The fork node stays in a LinearRegion (so it's placed at end of chain),
            // and the ForkRegion (branches only) becomes its Child.
            placed.Add(nodeId);

            var node = blueprint.GetNodeById(nodeId);
            Log.Debug("[Layout]   Fork node: {Name} ({Type}) → {BranchCount} branches [{Pins}]",
                node?.Name, node?.NodeType, targets.Count,
                string.Join(", ", targets.Select(t => t.PinName)));

            var branches = new List<LayoutRegion?>();
            foreach (var target in targets)
            {
                branches.Add(BuildRegionTree(target.TargetId, execMap, blueprint, visited, placed));
            }

            var linear = new LinearRegion(nodeId);
            linear.Child = new ForkRegion(nodeId, branches);
            return linear;
        }

        // Fallback: treat as linear
        placed.Add(nodeId);
        return new LinearRegion(nodeId);
    }

    /// <summary>
    /// Places data-only nodes (Const, Variable) in a left sidebar column.
    /// </summary>
    private void PlaceDataNodes(KitX.Core.Contract.Workflow.Blueprint blueprint, HashSet<string> placed)
    {
        var dataNodes = blueprint.Nodes.Where(n => !placed.Contains(n.Id)).ToList();
        if (dataNodes.Count == 0) return;

        Log.Debug("[Layout] Placing {Count} data nodes (Const, Variable, etc.)", dataNodes.Count);

        var variableNodes = dataNodes.Where(n => n.NodeType == BlueprintNodeType.Variable).ToList();
        var constNodes = dataNodes.Where(n => n.NodeType == BlueprintNodeType.Const).ToList();
        var otherNodes = dataNodes.Where(n =>
            n.NodeType != BlueprintNodeType.Variable
            && n.NodeType != BlueprintNodeType.Const).ToList();

        // Variable nodes in the far-left column
        for (int i = 0; i < variableNodes.Count; i++)
        {
            variableNodes[i].X = DataSidebarX;
            variableNodes[i].Y = YOffset + i * DataNodeVSpacing;
            placed.Add(variableNodes[i].Id);
        }

        // Const nodes in the constant column, starting below variables
        double constStartY = YOffset + variableNodes.Count * DataNodeVSpacing;
        for (int i = 0; i < constNodes.Count; i++)
        {
            constNodes[i].X = ConstSidebarX;
            constNodes[i].Y = constStartY + i * DataNodeVSpacing;
            placed.Add(constNodes[i].Id);
        }

        // Other data nodes: place near first consumer
        foreach (var node in otherNodes)
        {
            var firstConn = blueprint.Connections
                .FirstOrDefault(c => c.SourceNodeId == node.Id);
            if (firstConn != null)
            {
                var targetNode = blueprint.GetNodeById(firstConn.TargetNodeId);
                if (targetNode != null)
                {
                    node.X = targetNode.X - HSpacing;
                    node.Y = targetNode.Y;
                    placed.Add(node.Id);
                    continue;
                }
            }

            // No consumer found — place at bottom
            var maxY = blueprint.Nodes.Where(n => placed.Contains(n.Id))
                .Select(n => n.Y + n.Height).DefaultIfEmpty(0).Max();
            node.X = XOffset;
            node.Y = maxY + VSpacing;
            placed.Add(node.Id);
        }
    }

    #region Layout Region Types

    /// <summary>
    /// Abstract base for a measurable, arrangeable layout region
    /// </summary>
    private abstract class LayoutRegion
    {
        public double MeasuredWidth { get; protected set; }
        public double MeasuredHeight { get; protected set; }
        public abstract void Measure(double availableWidth = MaxRowWidth);
        public abstract void Arrange(double x, double y, KitX.Core.Contract.Workflow.Blueprint bp);
    }

    /// <summary>
    /// Linear chain of nodes, left-to-right, with smart wrapping at availableWidth
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

            // Build rows with wrapping relative to available width
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

            // Calculate dimensions
            MeasuredWidth = _rows.Count > 0
                ? _rows.Max(r => (r.Count - 1) * HSpacing + NodeWidth)
                : 0;
            MeasuredHeight = _rows.Count > 0
                ? (_rows.Count - 1) * VSpacing + NodeHeight
                : 0;

            // Include child region
            if (Child != null)
            {
                Child.Measure(availableWidth);
                MeasuredWidth = Math.Max(MeasuredWidth, Child.MeasuredWidth);
                MeasuredHeight += Child.MeasuredHeight > 0 ? VSpacing + Child.MeasuredHeight : 0;
            }
        }

        public override void Arrange(double x, double y, KitX.Core.Contract.Workflow.Blueprint bp)
        {
            double currentY = y;

            foreach (var row in _rows)
            {
                double currentX = x;
                foreach (var nodeId in row)
                {
                    var node = bp.GetNodeById(nodeId);
                    if (node != null)
                    {
                        node.X = currentX;
                        node.Y = currentY;
                    }
                    currentX += HSpacing;
                }
                currentY += VSpacing;
            }

            // Arrange child region
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
    /// Supports any number of execution output arms (2 for Branch/Loop, N for future nodes).
    /// </summary>
    private class ForkRegion : LayoutRegion
    {
        public string ForkNodeId;
        public List<LayoutRegion?> Branches;

        // Symmetric layout: upper / middle / lower branch groups
        private double _upperBranchHeight;
        private double _middleBranchHeight;

        public ForkRegion(string forkNodeId, List<LayoutRegion?> branches)
        {
            ForkNodeId = forkNodeId;
            Branches = branches;
        }

        /// <summary>
        /// Determines layout direction for a branch by its index.
        /// <para>-1 = upper-right, 0 = straight-right (same Y as fork), 1 = lower-right</para>
        /// <para>Rule: compare (index + 1) with (total + 1) / 2.0</para>
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
            // Branches use full availableWidth — ForkHGap is horizontal indent, not row-space
            double branchAvailableWidth = availableWidth;

            // Measure all branches
            foreach (var branch in Branches)
                branch?.Measure(branchAvailableWidth);

            // Width: gap + max branch width (fork node is in parent LinearRegion)
            double maxBranchWidth = Branches
                .Where(b => b != null)
                .Select(b => b!.MeasuredWidth)
                .DefaultIfEmpty(0)
                .Max();

            MeasuredWidth = ForkHGap + maxBranchWidth;

            // Height: symmetric layout — group branches by direction
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
                    case -1: // upper
                        if (_upperBranchHeight > 0) _upperBranchHeight += VSpacing;
                        _upperBranchHeight += branch.MeasuredHeight;
                        break;
                    case 0: // middle (straight-right)
                        _middleBranchHeight = branch.MeasuredHeight;
                        break;
                    case 1: // lower
                        if (lowerBranchHeight > 0) lowerBranchHeight += VSpacing;
                        lowerBranchHeight += branch.MeasuredHeight;
                        break;
                }
            }

            double middleHeight = _middleBranchHeight;

            MeasuredHeight = _upperBranchHeight
                + (_upperBranchHeight > 0 ? ForkVGap : 0)
                + middleHeight
                + (lowerBranchHeight > 0 ? ForkVGap : 0)
                + lowerBranchHeight;
        }

        public override void Arrange(double x, double y, KitX.Core.Contract.Workflow.Blueprint bp)
        {
            // Read the fork node's actual position (placed by parent LinearRegion)
            var forkNode = bp.GetNodeById(ForkNodeId);
            if (forkNode == null) return;

            double branchBaseX = forkNode.X + forkNode.Width + ForkHGap;

            // Upper branches start just above the fork node and flow downward.
            // Lower branches start below the fork node, but also below any upper branches
            // to avoid vertical overlap at the same X.
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
                    case -1: // upper-right
                        branch.Arrange(branchBaseX, currentUpperY, bp);
                        currentUpperY += branch.MeasuredHeight + VSpacing;
                        break;
                    case 0: // straight-right (same Y as fork)
                        branch.Arrange(branchBaseX, forkNode.Y, bp);
                        break;
                    case 1: // lower-right
                        branch.Arrange(branchBaseX, currentLowerY, bp);
                        currentLowerY += branch.MeasuredHeight + VSpacing;
                        break;
                }
            }
        }
    }

    #endregion
}
