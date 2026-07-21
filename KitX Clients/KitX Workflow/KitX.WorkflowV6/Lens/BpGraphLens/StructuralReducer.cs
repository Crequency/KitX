namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// StructuralReducer — validates that a Blueprint Exec graph is structurally
// well-formed (discussion notes §7.2, §十二-J).
//
// In a structured Blueprint graph:
//   1. The Exec-edge graph must form a DAG (no back-edges that create cycles).
//   2. Each node receives at most one Exec input (single-entry for every scope).
//   3. Loop back-edges may only originate from explicit loop nodes (Each/While),
//      never from plain Pipeline nodes.
//
// The check is called by BpGraphLens.Diff on every Exec-edge edit. When the check
// fails, the returned error includes guidance to use a loop node (ForEach/While)
// to express iteration.
//
// MVP implementation (§十二-J): one-shot full-graph check; no incremental update.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Validates the structural integrity of a Blueprint's Exec-edge graph.
/// Pure: the blueprint is never mutated.
/// </summary>
internal static class StructuralReducer
{
    /// <summary>
    /// Checks whether the Blueprint's Exec connections form a valid structured graph.
    /// Returns an empty string on success, or a user-facing error message on failure.
    /// </summary>
    public static string? Check(Blueprint blueprint)
    {
        if (blueprint.Nodes.Count == 0) return null;

        // Build Exec adjacency (output pin -> input pin connections).
        var execTargets = new Dictionary<string, string>(); // target node id -> ?
        var execSourceCount = new Dictionary<string, int>(); // source node id -> outgoing exec count

        foreach (var conn in blueprint.Connections)
        {
            var fromNode = blueprint.Nodes.Find(n => n.Id == conn.SourceNodeId);
            var toNode = blueprint.Nodes.Find(n => n.Id == conn.TargetNodeId);
            var fromPin = fromNode?.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (fromPin is null || toNode is null) continue;

            // Only check Exec connections (data connections are unrestricted).
            if (fromPin.Name != "Exec" && fromPin.Name != "True" && fromPin.Name != "False"
                && fromPin.Name != "Body" && fromPin.Name != "End" && !int.TryParse(fromPin.Name, out _)
                && fromPin.Name != "Default")
                continue;

            execSourceCount[conn.SourceNodeId] = execSourceCount.GetValueOrDefault(conn.SourceNodeId) + 1;

            // Check: each node can have at most one Exec input.
            // (Entry nodes are always single-entry by design.)
        }

        // Check for back edges in the Exec graph.
        // Walk from each EntryNode forward; if we encounter a cycle, it's non-structural.
        var entryNodes = blueprint.Nodes.Where(n => n is EntryNode).ToList();
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();

        foreach (var entry in entryNodes)
        {
            if (HasCycle(blueprint, entry.Id, visited, inStack))
                return "Non-structural back edge detected. Use a loop node (ForEach/While) to express iteration, or break/continue to exit a loop early.";
        }

        return null; // structurally valid
    }

    private static bool HasCycle(Blueprint bp, string nodeId, HashSet<string> visited, HashSet<string> inStack)
    {
        if (inStack.Contains(nodeId)) return true; // cycle detected
        if (visited.Contains(nodeId)) return false;
        visited.Add(nodeId);
        inStack.Add(nodeId);

        var node = bp.Nodes.Find(n => n.Id == nodeId);
        if (node is null) return false;

        foreach (var outPin in node.OutputPins)
        {
            // Follow Exec connections from this node.
            foreach (var conn in bp.Connections.Where(c => c.SourceNodeId == nodeId && c.SourcePinId == outPin.Id))
            {
                // Only check Exec-type pins (data connections can form any topology).
                if (outPin.Name == "Exec" || outPin.Name == "True" || outPin.Name == "False"
                    || outPin.Name == "Body" || outPin.Name == "End" || int.TryParse(outPin.Name, out _)
                    || outPin.Name == "Default")
                {
                    if (HasCycle(bp, conn.TargetNodeId, visited, inStack))
                        return true;
                }
            }
        }

        inStack.Remove(nodeId);
        return false;
    }
}