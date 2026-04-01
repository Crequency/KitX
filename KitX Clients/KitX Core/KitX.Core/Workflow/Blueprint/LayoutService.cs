using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint;

public class LayoutService : ILayoutService
{
    public void LayoutNodes(Contract.Workflow.Blueprint blueprint)
    {
        var depths = new Dictionary<string, int>();
        var visited = new HashSet<string>();

        // Phase 1: Calculate depths along exec chain
        var entry = blueprint.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
        if (entry != null)
        {
            depths[entry.Id] = 0;
            CalculateExecDepths(blueprint, entry, depths, visited);
        }

        // Phase 2: Place data nodes (Const etc.) at their consumer's depth
        foreach (var node in blueprint.Nodes)
        {
            if (depths.ContainsKey(node.Id)) continue;

            var firstConn = blueprint.Connections
                .FirstOrDefault(c => c.SourceNodeId == node.Id);
            if (firstConn != null && depths.ContainsKey(firstConn.TargetNodeId))
            {
                depths[node.Id] = depths[firstConn.TargetNodeId];
            }
        }

        // Phase 3: Remaining unplaced nodes appended after max depth
        var maxDepth = depths.Values.Count > 0 ? depths.Values.Max() : 0;
        foreach (var node in blueprint.Nodes)
        {
            if (!depths.ContainsKey(node.Id))
            {
                depths[node.Id] = ++maxDepth;
            }
        }

        // Phase 4: Group by depth + assign coordinates
        var depthGroups = new Dictionary<int, List<BlueprintNode>>();
        foreach (var node in blueprint.Nodes)
        {
            var depth = depths[node.Id];
            if (!depthGroups.ContainsKey(depth))
                depthGroups[depth] = new List<BlueprintNode>();
            depthGroups[depth].Add(node);
        }

        const double HSpacing = 220;
        const double VSpacing = 130;
        const double XOffset = 50;
        const double YOffset = 50;

        foreach (var group in depthGroups.OrderBy(g => g.Key))
        {
            var depth = group.Key;
            var sorted = group.Value
                .OrderBy(n => n.NodeType == BlueprintNodeType.Const ? 1 : 0)
                .ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].X = XOffset + depth * HSpacing;
                sorted[i].Y = YOffset + i * VSpacing;
            }
        }
    }

    /// <summary>
    /// Only traverse Execution-type connections for depth calculation,
    /// so data-dependency edges don't pull nodes into wrong layers.
    /// </summary>
    private void CalculateExecDepths(Contract.Workflow.Blueprint blueprint, BlueprintNode node,
        Dictionary<string, int> depths, HashSet<string> visited)
    {
        if (visited.Contains(node.Id)) return;
        visited.Add(node.Id);

        // Find output pins that are Exec type (or named "Exec" / "True" / "False" / "LoopBody" / "LoopEnd")
        var execOutPinIds = node.OutputPins
            .Where(p => p.Type == PinType.Execution)
            .Select(p => p.Id)
            .ToHashSet();

        var execConnections = blueprint.Connections.Where(c =>
            c.SourceNodeId == node.Id && execOutPinIds.Contains(c.SourcePinId));

        foreach (var conn in execConnections)
        {
            if (blueprint.GetNodeById(conn.TargetNodeId) is { } targetNode)
            {
                var currentDepth = depths[node.Id];
                if (!depths.ContainsKey(targetNode.Id) || depths[targetNode.Id] < currentDepth + 1)
                {
                    depths[targetNode.Id] = currentDepth + 1;
                }
                CalculateExecDepths(blueprint, targetNode, depths, visited);
            }
        }
    }
}
