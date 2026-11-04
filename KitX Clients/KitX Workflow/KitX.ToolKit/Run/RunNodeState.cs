namespace KitX.ToolKit.Run;

/// <summary>
/// Where a node of a run currently stands. Every node in the graph has exactly one of these
/// at all times, so the run can answer "what happened to every node" without the caller
/// reconstructing it from counters.
/// </summary>
/// <remarks>
/// <para>The three "never ran" states are deliberately distinct. <see cref="Skipped"/> means
/// an upstream failure made the node unreachable (ADR-0007 D3); <see cref="Cancelled"/> means
/// the run was stopped; <see cref="Pending"/> means it is still waiting on its AND-join.
/// Collapsing them would make a truncated run indistinguishable from a run that is merely
/// slow, and a cancelled node indistinguishable from a skipped one.</para>
/// </remarks>
internal enum RunNodeState
{
    /// <summary>Not yet reached: the AND-join counter is still positive.</summary>
    Pending,

    /// <summary>
    /// The AND-join is satisfied and the node is claimed, but no execution slot has been
    /// granted yet. Distinguishing this from <see cref="Running"/> keeps the concurrency
    /// limit honest: a queued node has not started, so it must not count as in-flight.
    /// </summary>
    Activated,

    /// <summary>The node body is in flight.</summary>
    Running,

    /// <summary>The node body ran to completion.</summary>
    Completed,

    /// <summary>The node body ran and failed.</summary>
    Failed,

    /// <summary>The node was cancelled before or during execution.</summary>
    Cancelled,

    /// <summary>The node never ran because a predecessor failed and truncated the run.</summary>
    Skipped,
}
