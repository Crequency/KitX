namespace KitX.ToolKit.Compile;

/// <summary>
/// A directed edge in the workflow-completion graph: <see cref="From"/> finishing starts
/// <see cref="To"/> (ADR-0002 D3).
/// </summary>
/// <param name="From">The workflow whose completion triggers the edge.</param>
/// <param name="To">The workflow started by the edge.</param>
/// <remarks>
/// <para><b>An edge carries topology and nothing else</b> (design Q44). It used to carry the
/// <c>TriggerBinding</c> that produced it, which meant a completion edge could declare
/// parameter injection — and the scheduler read those parameters from the previous node's
/// output packet. That made two prefixes, <c>$payload.</c> and <c>$output.</c>, resolve against
/// the same thing: a payload reference written on a completion edge silently read the
/// predecessor's output, and an output reference written on a trigger binding silently read the
/// trigger payload. Neither was a diagnosable mistake because both branches called the same
/// extraction routine.</para>
///
/// <para>The downstream node receives the predecessor's whole output packet instead. That is
/// what "data flows along the DAG" (Q48) means: there is nothing to select, so nothing to
/// get wrong.</para>
/// </remarks>
internal sealed record WorkflowEdge(string From, string To);
