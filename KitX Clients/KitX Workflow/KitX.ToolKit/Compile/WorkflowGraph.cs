using KitX.ToolKit.Contract.Declaration;

namespace KitX.ToolKit.Compile;

/// <summary>
/// The workflow-completion graph — <b>one</b> object, read by validation, scheduling, export
/// and the canvas (ADR-0002 D3).
///
/// <para><b>It carries no semantics.</b> There is no "predecessor count" property, no
/// "is acyclic" flag, no join policy. It is nodes, edges, and a successor lookup. Each
/// consumer applies its own meaning — the scheduler counts fan-in, the exporter draws arrows,
/// the canvas lays out boxes. The old code let four consumers each re-derive the edges from
/// config, and the validation and scheduling versions had already diverged (one de-duplicated
/// by <c>To</c>, the other accumulated per binding).</para>
///
/// <para>Duplicate <c>(From, To)</c> pairs cannot occur here: <c>ToolkitCompiler</c> rejects
/// them (ADR-0002 D5). That is what makes the two old readings equivalent instead of having to
/// keep both of them careful.</para>
/// </summary>
internal sealed class WorkflowGraph
{
    private readonly Dictionary<string, List<WorkflowEdge>> _byFrom;

    /// <summary>Every workflow id that participates in at least one edge, sorted ordinally so
    /// that iteration order is stable across runs (diagnostics and export depend on it).</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>Every edge. Order follows the config's declaration order.</summary>
    public IReadOnlyList<WorkflowEdge> Edges { get; }

    internal WorkflowGraph(IReadOnlyList<WorkflowEdge> edges, IEnumerable<string>? declaredNodes = null)
    {
        Edges = edges;

        _byFrom = new Dictionary<string, List<WorkflowEdge>>(StringComparer.Ordinal);
        var nodes = new HashSet<string>(StringComparer.Ordinal);

        if (declaredNodes is not null)
            foreach (var id in declaredNodes)
                if (!string.IsNullOrWhiteSpace(id))
                    nodes.Add(id);

        foreach (var edge in edges)
        {
            if (!_byFrom.TryGetValue(edge.From, out var list))
                _byFrom[edge.From] = list = [];
            list.Add(edge);

            nodes.Add(edge.From);
            nodes.Add(edge.To);
        }

        var sorted = nodes.ToList();
        sorted.Sort(StringComparer.Ordinal);
        Nodes = sorted;
    }

    /// <summary>Edges leaving <paramref name="workflowId"/>. Empty (never null) when it has none.</summary>
    public IReadOnlyList<WorkflowEdge> Successors(string workflowId)
        => _byFrom.TryGetValue(workflowId, out var list) ? list : [];

    /// <summary>
    /// Number of distinct predecessors of <paramref name="workflowId"/> — the scheduler's
    /// join arity. Lives here as a <i>query over the graph</i>, not as stored state, so it
    /// cannot go stale relative to <see cref="Edges"/>.
    /// </summary>
    public int PredecessorCount(string workflowId)
        => Edges.Count(e => string.Equals(e.To, workflowId, StringComparison.Ordinal));

    /// <summary>True when no edge references <paramref name="workflowId"/> as a target.</summary>
    public bool IsEntry(string workflowId) => PredecessorCount(workflowId) == 0;

    /// <summary>
    /// Builds a graph from an edge list, adding any declared node that no edge mentions as an
    /// isolated node.
    /// </summary>
    /// <remarks>
    /// Isolated nodes matter: a workflow with no edges is still runnable when a trigger names
    /// it directly. Omitting it would make <see cref="Nodes"/> an incomplete answer to "what
    /// can run", and the scheduler seeds its join counters from that answer.
    /// </remarks>
    internal static WorkflowGraph FromEdges(IEnumerable<WorkflowEdge> edges, IEnumerable<string>? declaredNodes = null)
        => new(edges as IReadOnlyList<WorkflowEdge> ?? edges.ToList(), declaredNodes);
}
