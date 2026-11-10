using System;
using System.Collections.Generic;

namespace KitX.ToolKit.Contract;

/// <summary>
/// Finds a cycle in a directed graph, once, for every caller that needs to know whether a set of
/// workflow-completion edges is a strict DAG.
/// </summary>
/// <remarks>
/// <para><b>Why this is a shared function and not two local ones.</b> The editor's
/// <see cref="ConfigValidator"/> and the compiler each had their own depth-first search. They
/// emitted the same sentence — that part had already been factored out into
/// <see cref="ToolkitDiagnosticText"/> — but they were still two algorithms producing that
/// sentence, and <c>Mount</c> runs both on the same config, one after the other. Two searches that
/// agree today agree for as long as nobody edits either one; the day one of them learns about a
/// shape the other does not, a config can pass one and fail the other, and nothing in the code says
/// which answer is right.</para>
///
/// <para><b>What is deliberately not shared.</b> Only the search is shared, not the graph. The
/// editor builds its adjacency from declared <c>Triggers[].Bindings[]</c>; the compiler builds it
/// from compiled workflows. Those really are different inputs — one is a document, the other is the
/// result of compiling that document — and collapsing them would mean the editor had to compile
/// before it could answer a keystroke, which is the one thing it must not do. So each side keeps
/// deciding what counts as an edge, and hands the edges here.</para>
///
/// <para><b>Why the signature takes edges rather than a graph type.</b> The contract assembly does
/// not know <c>WorkflowGraph</c> (it lives in the implementation, and must not be referenced from
/// here). A list of pairs says everything a cycle search needs and nothing about who built it.</para>
///
/// <para><b>Deterministic, and that matters.</b> Nodes are visited in ordinal order, and each
/// node's successors are visited in the order given. Without the first rule the reported cycle
/// depends on dictionary iteration order, so two runs on the same config could name different
/// cycles — and the test that pins the message would be pinning a coin flip.</para>
/// </remarks>
public static class WorkflowCycle
{
    /// <summary>
    /// Returns the first cycle found as a path whose last element repeats the first, or
    /// <see langword="null"/> when the graph is acyclic.
    /// </summary>
    /// <param name="nodes">
    /// Every node to consider. Nodes reachable only through <paramref name="edges"/> are included
    /// even when absent here, so a caller that has only the edges can pass an empty list.
    /// </param>
    /// <param name="edges">The directed edges, <c>(From, To)</c>.</param>
    /// <remarks>
    /// <para><b>The returned path repeats its first node at the end</b>
    /// (<c>A, B, C, A</c>) because that is what makes it read as a cycle rather than as a list of
    /// three workflows. The caller joins it; this function does no formatting.</para>
    ///
    /// <para><b>First cycle only.</b> Reporting every cycle in a graph that is already rejected
    /// gives the author a longer list to fix one at a time. A single cycle is enough to say "this
    /// shape is not a DAG".</para>
    /// </remarks>
    public static IReadOnlyList<string>? Find(
        IEnumerable<string> nodes,
        IEnumerable<(string From, string To)> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var all = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node))
                continue;

            all.Add(node);
        }

        foreach (var (from, to) in edges)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                continue;

            if (!outgoing.TryGetValue(from, out var successors))
                outgoing[from] = successors = [];

            successors.Add(to);
            all.Add(from);
            all.Add(to);
        }

        // 0 = unvisited, 1 = on the current path, 2 = fully explored.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in all)
            state[node] = 0;

        var path = new List<string>();

        foreach (var start in all)
        {
            if (state[start] != 0)
                continue;

            if (Visit(start) is { } cycle)
                return cycle;
        }

        return null;

        List<string>? Visit(string node)
        {
            state[node] = 1;
            path.Add(node);

            if (outgoing.TryGetValue(node, out var successors))
            {
                foreach (var next in successors)
                {
                    if (state[next] == 1)
                    {
                        // The cycle is the tail of the path from wherever `next` was first entered,
                        // plus `next` again to close it.
                        var cycleStart = path.IndexOf(next);
                        var cycle = new List<string>(path.Count - cycleStart + 1);
                        for (var i = cycleStart; i < path.Count; i++)
                            cycle.Add(path[i]);

                        cycle.Add(next);
                        return cycle;
                    }

                    if (state[next] == 0 && Visit(next) is { } found)
                        return found;
                }
            }

            path.RemoveAt(path.Count - 1);
            state[node] = 2;

            return null;
        }
    }
}
