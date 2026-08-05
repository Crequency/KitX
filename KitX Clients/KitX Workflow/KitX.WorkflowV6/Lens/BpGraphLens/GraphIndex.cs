namespace KitX.WorkflowV6.Lens.BpGraphLens;

using KitX.Core.Contract.Workflow;

// ─────────────────────────────────────────────────────────────────────────────
// GraphIndex — immutable query facade over a Blueprint's node/connection graph.
//
// Built once in O(V+E) at the start of reverse translation, it answers the
// per-node / per-pin connectivity queries that BpReverseTranslator used to
// answer by linear-scans of _bp.Connections (6 query helpers + 5 hand-written
// "find the incoming edge source" loops → O(E²) on deep exec chains).
//
// Semantic-convergence contract (each query must reproduce the original scan
// EXACTLY):
//   • Per-key edge lists preserve GLOBAL CONNECTION ORDER, so first-match
//     queries (IncomingTo, ReadDataInput, ReorderSourcesByPinOrder,
//     ReconstructPipelineOrCall, BuildKsCallFromFunctionNode,
//     PreMarkControlFlowConsumed) return the same edge the original `break`-
//     on-first-match loops returned.
//   • Pin-TYPE filtering (exec vs data) is applied PER QUERY, mirroring each
//     original predicate: HasOutgoingDataEdge / HasIncomingDataEdge /
//     HasIncomingDataFrom / MarkConsumedSubtree filter the SOURCE pin;
//     IsWriteVarTap's outgoing check filters the TARGET pin;
//     HasWiredInputs / the source-picking loops apply no pin filter.
//   • Connections whose source OR target NODE does not resolve are dropped at
//     build time. The original IndexGraph dropped them from the exec index too;
//     the only per-query sites that ever saw them were PreMarkControlFlowConsumed
//     and ReorderSourcesByPinOrder (which skip unresolved sources and continue
//     scanning). With strong-constraint editing such dangling connections cannot
//     exist, and no test exercises them — the queries above therefore converge
//     on the resolved-edge first-match.
//   • Two exec indices: _execOut (STRICT — both pins must resolve to Execution
//     pins; BpReverseTranslator's original predicate) and _execOutLoose (SOURCE
//     pin only — the original ScopeAnalyzer/LayoutService/StructuralReducer scans
//     never inspected the target pin, and the KS105 back-edge test feeds a node
//     whose input pin does not resolve). Per-consumer queries pick the variant
//     that matches their original scan.
//   • Pin ids that do not resolve on their node stay in the edge lists as null
//     so per-query filters can skip them exactly like the original `Find` +
//     `is null → continue` pattern.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Immutable query facade over a <see cref="Blueprint"/>'s connection graph.
/// </summary>
internal sealed class GraphIndex
{
    /// <summary>A fully node-resolved connection (pins may still be unresolvable — null).</summary>
    internal readonly record struct Edge(
        BlueprintNode Source,
        BlueprintPin? SourcePin,
        BlueprintNode Target,
        BlueprintPin? TargetPin);

    private readonly Dictionary<string, BlueprintNode> _byId;

    // Outgoing exec edges: (sourceNodeId, sourcePinName) → target nodes, in connection order.
    // STRICT: both endpoints must resolve to an Execution pin (BpReverseTranslator's
    // original scan inspected the target pin too). See _execOutLoose for the
    // source-pin-only variant used by the scope walkers.
    private readonly Dictionary<(string, string), List<BlueprintNode>> _execOut = new();

    // Outgoing exec edges filtered by the SOURCE pin only: every edge whose source pin
    // resolves to an Execution pin, regardless of the target pin. Mirrors the original
    // scans of ScopeAnalyzer / LayoutService / StructuralReducer, which never inspected
    // the target pin — in particular the b→Entry back-edge of the KS105 test (target
    // pin does not resolve) must stay visible here or the cycle check would miss it.
    private readonly Dictionary<(string, string), List<BlueprintNode>> _execOutLoose = new();

    // Data-side edges by source node id, in connection order.
    private readonly Dictionary<string, List<Edge>> _outgoingByNode = new();

    // Data-side edges by target node id, in connection order.
    private readonly Dictionary<string, List<Edge>> _incomingByNode = new();

    // Data-side edges by (target node id, target pin id), in connection order.
    private readonly Dictionary<(string, string), List<Edge>> _incomingByPin = new();

    // Raw connections by node id (both endpoints; node/pin resolution NOT required) —
    // backs HasAnyConnection, which must match the original HasNoConnections predicate
    // (that predicate matched connection node ids only, no pin lookups).
    private readonly Dictionary<string, List<BlueprintConnection>> _connsByNode = new();

    public GraphIndex(Blueprint bp)
    {
        _byId = bp.Nodes.ToDictionary(n => n.Id);

        foreach (var conn in bp.Connections)
        {
            AddRaw(_connsByNode, conn.SourceNodeId, conn);
            AddRaw(_connsByNode, conn.TargetNodeId, conn);

            if (!_byId.TryGetValue(conn.SourceNodeId, out var src)) continue;
            if (!_byId.TryGetValue(conn.TargetNodeId, out var tgt)) continue;

            var srcPin = src.OutputPins.Find(p => p.Id == conn.SourcePinId);
            var tgtPin = tgt.InputPins.Find(p => p.Id == conn.TargetPinId);

            if (srcPin is not null && srcPin.Type == PinType.Execution)
            {
                // Loose exec edge (source-pin-only filter — the original scope-walk
                // scans never inspected the target pin; a back-edge whose target pin
                // does not resolve still counts as an exec edge for cycle/walk checks).
                var looseKey = (conn.SourceNodeId, srcPin.Name);
                if (!_execOutLoose.TryGetValue(looseKey, out var looseList))
                {
                    looseList = new List<BlueprintNode>();
                    _execOutLoose[looseKey] = looseList;
                }
                looseList.Add(tgt);

                // Strict exec edge: additionally requires the target pin to resolve to
                // an Execution pin (BpReverseTranslator's original predicate).
                if (tgtPin is not null && tgtPin.Type == PinType.Execution)
                {
                    var key = (conn.SourceNodeId, srcPin.Name);
                    if (!_execOut.TryGetValue(key, out var list))
                    {
                        list = new List<BlueprintNode>();
                        _execOut[key] = list;
                    }
                    list.Add(tgt);
                    continue;
                }
            }

            // Data-side edge (either pin may be null — per-query filters decide).
            var edge = new Edge(src, srcPin, tgt, tgtPin);
            AddEdge(_outgoingByNode, conn.SourceNodeId, edge);
            AddEdge(_incomingByNode, conn.TargetNodeId, edge);
            if (tgtPin is not null)
                AddEdge(_incomingByPin, (conn.TargetNodeId, conn.TargetPinId), edge);
        }
    }

    private static void AddRaw(Dictionary<string, List<BlueprintConnection>> map, string nodeId, BlueprintConnection conn)
    {
        if (!map.TryGetValue(nodeId, out var list))
        {
            list = new List<BlueprintConnection>();
            map[nodeId] = list;
        }
        list.Add(conn);
    }

    private static void AddEdge(Dictionary<string, List<Edge>> map, string nodeId, Edge edge)
    {
        if (!map.TryGetValue(nodeId, out var list))
        {
            list = new List<Edge>();
            map[nodeId] = list;
        }
        list.Add(edge);
    }

    private static void AddEdge(Dictionary<(string, string), List<Edge>> map, (string, string) key, Edge edge)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<Edge>();
            map[key] = list;
        }
        list.Add(edge);
    }

    // ── Exec index (was _execOut) ──

    public bool TryGetExecTargets(string nodeId, string pinName, out List<BlueprintNode> targets)
        => _execOut.TryGetValue((nodeId, pinName), out targets!);

    public bool HasExecTargets(string nodeId, string pinName)
        => _execOut.ContainsKey((nodeId, pinName));

    /// <summary>
    /// Loose exec targets: every edge whose SOURCE pin resolves to an Execution pin,
    /// in connection order (the TARGET pin is not inspected). Mirrors the per-consumer
    /// scans of ScopeAnalyzer / LayoutService / StructuralReducer, which filtered the
    /// source pin only — the strict <see cref="TryGetExecTargets"/> additionally
    /// requires the target pin to resolve to an Execution pin.
    /// </summary>
    public bool TryGetLooseExecTargets(string nodeId, string pinName, out List<BlueprintNode> targets)
        => _execOutLoose.TryGetValue((nodeId, pinName), out targets!);

    // ── Node / raw-connection queries ──

    /// <summary>All nodes of the blueprint (dictionary order — for enumeration only).</summary>
    public IEnumerable<BlueprintNode> Nodes => _byId.Values;

    public BlueprintNode? GetNode(string id) => _byId.GetValueOrDefault(id);

    /// <summary>True when any connection (either endpoint, resolution-independent) touches the node.</summary>
    public bool HasAnyConnection(BlueprintNode node) => _connsByNode.ContainsKey(node.Id);

    // ── Data-edge queries (replicated original scan predicates) ──

    /// <summary>
    /// True when the node has at least one outgoing edge whose SOURCE pin resolves to
    /// a data pin (the original HasOutgoingDataEdge predicate — the target pin was
    /// not inspected).
    /// </summary>
    public bool HasOutgoingDataEdge(BlueprintNode node)
    {
        if (!_outgoingByNode.TryGetValue(node.Id, out var edges)) return false;
        foreach (var e in edges)
            if (e.SourcePin is not null && e.SourcePin.Type != PinType.Execution)
                return true;
        return false;
    }

    /// <summary>
    /// True when the node has at least one incoming edge whose SOURCE pin resolves to
    /// a data pin (the original HasIncomingDataEdge predicate).
    /// </summary>
    public bool HasIncomingDataEdge(BlueprintNode node)
    {
        if (!_incomingByNode.TryGetValue(node.Id, out var edges)) return false;
        foreach (var e in edges)
            if (e.SourcePin is not null && e.SourcePin.Type != PinType.Execution)
                return true;
        return false;
    }

    /// <summary>
    /// True when <paramref name="node"/> has an incoming edge from <paramref name="src"/>
    /// whose source pin resolves to a data pin (covers the original DataComesFrom and
    /// HasDataInputFrom predicates — they were identical modulo the node type).
    /// </summary>
    public bool HasIncomingDataFrom(BlueprintNode node, BlueprintNode src)
    {
        if (!_incomingByNode.TryGetValue(node.Id, out var edges)) return false;
        foreach (var e in edges)
            if (ReferenceEquals(e.Source, src)
                && e.SourcePin is not null && e.SourcePin.Type != PinType.Execution)
                return true;
        return false;
    }

    /// <summary>
    /// Write-type var tap: a VariableNode whose Value input has an incoming data edge
    /// AND no outgoing edge whose TARGET pin resolves to a data pin (the original
    /// IsWriteVarTap predicate — its outgoing check inspected the target pin, unlike
    /// HasOutgoingDataEdge which inspects the source pin).
    /// </summary>
    public bool IsWriteVarTap(BlueprintNode node)
    {
        if (node is not VariableNode vn) return false;
        if (!HasIncomingDataEdge(vn)) return false;
        if (_outgoingByNode.TryGetValue(vn.Id, out var outEdges))
        {
            foreach (var e in outEdges)
                if (e.TargetPin is not null && e.TargetPin.Type != PinType.Execution)
                    return false;
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="fn"/> has a connection targeting any of its non-Exec
    /// input pins (the original HasWiredInputs predicate; connections whose SOURCE
    /// node does not resolve were never stored — see the header contract).
    /// </summary>
    public bool HasWiredInputs(BuiltinFunctionNode fn)
    {
        foreach (var pin in fn.InputPins)
        {
            if (pin.Name == BpPinNames.Exec) continue;
            if (_incomingByPin.ContainsKey((fn.Id, pin.Id))) return true;
        }
        return false;
    }

    // ── Raw edge lists (first-match / filtered iteration at call sites) ──

    /// <summary>All data-side edges targeting (nodeId, pinId), in connection order.</summary>
    public IReadOnlyList<Edge>? IncomingTo(string nodeId, string pinId)
        => _incomingByPin.TryGetValue((nodeId, pinId), out var list) ? list : null;

    /// <summary>All data-side edges targeting nodeId, in connection order.</summary>
    public IReadOnlyList<Edge>? IncomingToNode(string nodeId)
        => _incomingByNode.TryGetValue(nodeId, out var list) ? list : null;

    /// <summary>All data-side edges originating from nodeId, in connection order.</summary>
    public IReadOnlyList<Edge>? OutgoingFrom(string nodeId)
        => _outgoingByNode.TryGetValue(nodeId, out var list) ? list : null;
}
