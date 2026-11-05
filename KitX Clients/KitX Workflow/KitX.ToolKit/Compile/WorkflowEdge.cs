namespace KitX.ToolKit.Compile;

/// <summary>
/// A directed edge in the workflow-completion graph: <see cref="From"/> finishing starts
/// <see cref="To"/> (ADR-0002 D3).
/// </summary>
/// <remarks>
/// <para><b>An edge carries topology and names, never values</b> (design Q44 + Q70). It used to carry the
/// <c>TriggerBinding</c> that produced it, which meant a completion edge could declare
/// parameter injection — and the scheduler read those parameters from the previous node's
/// output packet. That made two prefixes, <c>$payload.</c> and <c>$output.</c>, resolve against
/// the same thing: a payload reference written on a completion edge silently read the
/// predecessor's output, and an output reference written on a trigger binding silently read the
/// trigger payload. Neither was a diagnosable mistake because both branches called the same
/// extraction routine.</para>
///
/// <para>The downstream node receives the predecessor's output packet instead. That is what
/// "data flows along the DAG" (Q48) means.</para>
///
/// <para><b>An edge may name what it consumes</b> (design Q70). <see cref="Consumes"/> is a set
/// of <i>property names</i>, not values: it says which names from the predecessor's output
/// packet the downstream node is allowed to see. The empty set is legal and is the recommended
/// default — it means "I read nothing from the packet", and it is what keeps an upstream
/// author's choice of property names from silently changing a downstream author's constants
/// (report §3.7: a string <c>count</c> arriving from upstream used to crash a downstream
/// <c>const count: int</c> at run time, with an error that named neither).</para>
///
/// <para>Names only, never values, is what preserves Q44: there is still nothing on the edge to
/// edit or to get out of sync with the code.</para>
/// </remarks>
/// <param name="From">The workflow whose completion triggers the edge.</param>
/// <param name="To">The workflow started by the edge.</param>
/// <param name="Consumes">
/// Property names this edge forwards from the predecessor's output packet. Empty means none —
/// the downstream node sees no packet properties at all (its declared constants and trigger
/// bindings still apply).
/// </param>
internal sealed record WorkflowEdge(string From, string To, IReadOnlySet<string> Consumes);
