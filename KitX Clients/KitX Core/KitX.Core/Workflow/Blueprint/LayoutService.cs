using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.Blueprint;

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
    private const double ForkVGap = 60;
    private const double ForkHGap = 40;
    private const double MaxRowWidth = 1200;
    private const double XOffset = 50;
    private const double YOffset = 50;
    private const double NodeWidth = 200;
    private const double NodeHeight = 100;

    /// <inheritdoc />
    public void LayoutNodes(Contract.Workflow.Blueprint blueprint)
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

        // Phase 5: Place data nodes near their consumers
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
    /// Builds nodeId → [(pinName, targetNodeId)] mapping for exec-type connections only
    /// </summary>
    private Dictionary<string, List<(string PinName, string TargetId)>> BuildExecAdjacencyMap(
        Contract.Workflow.Blueprint blueprint)
    {
        var map = new Dictionary<string, List<(string, string)>>();

        foreach (var conn in blueprint.Connections)
        {
            var sourceNode = blueprint.GetNodeById(conn.SourceNodeId);
            if (sourceNode == null) continue;

            var sourcePin = sourceNode.OutputPins.FirstOrDefault(p => p.Id == conn.SourcePinId);
            if (sourcePin == null || sourcePin.Type != PinType.Execution) continue;

            if (!map.ContainsKey(conn.SourceNodeId))
                map[conn.SourceNodeId] = [];
            map[conn.SourceNodeId].Add((sourcePin.Name, conn.TargetNodeId));
        }

        Log.Debug("[Layout] Exec adjacency map: {Count} nodes with exec outputs", map.Count);
        return map;
    }

    /// <summary>
    /// Recursively builds a tree of LayoutRegions from the exec chain.
    /// </summary>
    private LayoutRegion? BuildRegionTree(
        string nodeId,
        Dictionary<string, List<(string PinName, string TargetId)>> execMap,
        Contract.Workflow.Blueprint blueprint,
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
            // Fork: Branch (True/False), Loop (LoopBody/LoopEnd), or any N-way control flow
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

            return new ForkRegion(nodeId, branches);
        }

        // Fallback: 3+ exec outputs (treat as linear)
        placed.Add(nodeId);
        return new LinearRegion(nodeId);
    }

    /// <summary>
    /// Places data-only nodes (Const, etc.) near their first consumer
    /// </summary>
    private void PlaceDataNodes(Contract.Workflow.Blueprint blueprint, HashSet<string> placed)
    {
        var dataNodes = blueprint.Nodes.Where(n => !placed.Contains(n.Id)).ToList();
        if (dataNodes.Count == 0) return;

        Log.Debug("[Layout] Placing {Count} data nodes (Const, etc.)", dataNodes.Count);

        // Group data nodes by their first consumer target
        var dataNodeIndex = 0;
        foreach (var node in dataNodes)
        {
            var firstConn = blueprint.Connections
                .FirstOrDefault(c => c.SourceNodeId == node.Id);
            if (firstConn != null)
            {
                var targetNode = blueprint.GetNodeById(firstConn.TargetNodeId);
                if (targetNode != null)
                {
                    // Place above-left of the consumer
                    node.X = targetNode.X - HSpacing;
                    node.Y = targetNode.Y - (dataNodeIndex + 1) * (NodeHeight * 0.6);
                    placed.Add(node.Id);
                    dataNodeIndex++;
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
        public abstract void Arrange(double x, double y, Contract.Workflow.Blueprint bp);
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
                ? _rows.Max(r => r.Count * NodeWidth + Math.Max(0, r.Count - 1) * (HSpacing - NodeWidth))
                : 0;
            MeasuredHeight = _rows.Count > 0
                ? _rows.Count * NodeHeight + Math.Max(0, _rows.Count - 1) * (VSpacing - NodeHeight)
                : 0;

            // Include child region
            if (Child != null)
            {
                Child.Measure(availableWidth);
                MeasuredWidth = Math.Max(MeasuredWidth, Child.MeasuredWidth);
                MeasuredHeight += Child.MeasuredHeight > 0 ? VSpacing + Child.MeasuredHeight : 0;
            }
        }

        public override void Arrange(double x, double y, Contract.Workflow.Blueprint bp)
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
    /// Fork region: a fork node (Branch/Loop/etc.) with N sub-branches
    /// arranged vertically, indented to the right.
    /// Supports any number of execution output arms (2 for Branch/Loop, N for future nodes).
    /// </summary>
    private class ForkRegion : LayoutRegion
    {
        public string ForkNodeId;
        public List<LayoutRegion?> Branches;

        // Backward-compatible convenience properties for 2-branch case
        public LayoutRegion? UpperBranch => Branches.Count > 0 ? Branches[0] : null;
        public LayoutRegion? LowerBranch => Branches.Count > 1 ? Branches[1] : null;

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
            // Branches are indented to the right of the fork node
            double branchAvailableWidth = Math.Max(NodeWidth, availableWidth - NodeWidth - ForkHGap);

            // Measure all branches
            foreach (var branch in Branches)
                branch?.Measure(branchAvailableWidth);

            // Width: fork node + gap + max branch width (unchanged)
            double maxBranchWidth = Branches
                .Where(b => b != null)
                .Select(b => b!.MeasuredWidth)
                .DefaultIfEmpty(0)
                .Max();

            MeasuredWidth = NodeWidth + ForkHGap + maxBranchWidth;

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

            double middleAndForkHeight = Math.Max(NodeHeight, _middleBranchHeight);

            MeasuredHeight = _upperBranchHeight
                + (_upperBranchHeight > 0 ? ForkVGap : 0)
                + middleAndForkHeight
                + (lowerBranchHeight > 0 ? ForkVGap : 0)
                + lowerBranchHeight;
        }

        public override void Arrange(double x, double y, Contract.Workflow.Blueprint bp)
        {
            double branchX = x + NodeWidth + ForkHGap;

            // Fork node Y: offset down by upper branch height
            double forkY = y + _upperBranchHeight
                + (_upperBranchHeight > 0 ? ForkVGap : 0);

            // Place fork node
            var forkNode = bp.GetNodeById(ForkNodeId);
            if (forkNode != null)
            {
                forkNode.X = x;
                forkNode.Y = forkY;
            }

            // Arrange branches by direction
            double currentUpperY = y;
            double middleAndForkHeight = Math.Max(NodeHeight, _middleBranchHeight);
            double currentLowerY = forkY + middleAndForkHeight + ForkVGap;

            for (int i = 0; i < Branches.Count; i++)
            {
                var branch = Branches[i];
                if (branch == null || branch.MeasuredHeight <= 0) continue;

                int dir = GetBranchDirection(i, Branches.Count);
                switch (dir)
                {
                    case -1: // upper-right
                        branch.Arrange(branchX, currentUpperY, bp);
                        currentUpperY += branch.MeasuredHeight + VSpacing;
                        break;
                    case 0: // straight-right (same Y as fork)
                        branch.Arrange(branchX, forkY, bp);
                        break;
                    case 1: // lower-right
                        branch.Arrange(branchX, currentLowerY, bp);
                        currentLowerY += branch.MeasuredHeight + VSpacing;
                        break;
                }
            }
        }
    }

    #endregion
}
