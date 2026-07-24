namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// StructuralReducer — validates that a Blueprint Exec graph is structurally
// well-formed (discussion notes §7.2, §十二-J).
//
// In a structured Blueprint graph:
//   1. The Exec-edge graph must form a DAG (no back-edges that create cycles).
//   2. Non-Exec input pins receive at most one data connection (single-assignment data flow);
//      Exec inputs may receive multiple connections from merged control-flow tails,
//      e.g. if/else branches flowing to the same next statement.
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

        // Build node ID lookup for data-flow constraint check.
        var nodeById = blueprint.Nodes.ToDictionary(n => n.Id);

        // Constraint: each non-Exec input pin receives at most one connection
        // (single-assignment data flow). Exec inputs may receive multiple
        // connections from merged control-flow tails (e.g. if/else branches).
        var dataInputConnectionCount = new Dictionary<(string NodeId, string PinId), int>();
        foreach (var conn in blueprint.Connections)
        {
            var targetNode = nodeById.GetValueOrDefault(conn.TargetNodeId);
            if (targetNode is null) continue;
            var targetPin = targetNode.InputPins.FirstOrDefault(p => p.Id == conn.TargetPinId);
            if (targetPin is null) continue;
            if (targetPin.Type == PinType.Execution) continue; // Exec allows multi-merge

            var key = (conn.TargetNodeId, conn.TargetPinId);
            dataInputConnectionCount[key] = dataInputConnectionCount.GetValueOrDefault(key) + 1;
        }

        var dataErrors = new List<string>();
        foreach (var ((nodeId, pinId), count) in dataInputConnectionCount)
        {
            if (count > 1)
                dataErrors.Add($"Node '{nodeId}' input pin '{pinId}' has {count} incoming data connections — data flow requires single assignment (pin must have at most one incoming data wire).");
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

        if (dataErrors.Count > 0)
            return string.Join("\n", dataErrors);

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
                if (IsExecOutPin(outPin))
                {
                    if (HasCycle(bp, conn.TargetNodeId, visited, inStack))
                        return true;
                }
            }
        }

        inStack.Remove(nodeId);
        return false;
    }

    // TODO(C3): extract these pin-name literals to a shared BpPinNames constant class.
    private static bool IsExecPinName(string name) =>
        name == "Exec" || name == "True" || name == "False"
        || name == "Body" || name == "End" || name == "Default"
        || int.TryParse(name, out _);

    private static bool IsExecOutPin(BlueprintPin pin) =>
        pin.Direction == PinDirection.Output
        && pin.Type == PinType.Execution
        && IsExecPinName(pin.Name);

    private static bool IsExecInPin(BlueprintPin pin) =>
        pin.Direction == PinDirection.Input
        && pin.Type == PinType.Execution
        && pin.Name == "Exec";
}