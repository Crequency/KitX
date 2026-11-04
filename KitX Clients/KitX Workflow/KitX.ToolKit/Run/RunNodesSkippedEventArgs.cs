namespace KitX.ToolKit.Run;

/// <summary>
/// Raised when a failure truncated the run: these nodes will never execute because an
/// upstream node failed (ADR-0007 D3).
/// </summary>
/// <remarks>
/// <para>The event travels on the same channel as node completion on purpose. A downstream
/// node of a failure is otherwise indistinguishable from a node that was never in the graph —
/// the failure truncates, so nothing ever starts, so nothing ever reports. Consumers that
/// only count completions would conclude the run "finished early" rather than "was cut short".</para>
///
/// <para>One event is raised per truncation wave, carrying every node that wave made
/// unreachable. Naming the failed node lets a consumer attribute the wave without correlating
/// against the completion stream.</para>
/// </remarks>
internal sealed class RunNodesSkippedEventArgs : EventArgs
{
    public RunNodesSkippedEventArgs(string runId, IReadOnlyList<string> nodeIds, string? failedNodeId)
    {
        RunId = runId ?? throw new ArgumentNullException(nameof(runId));
        NodeIds = nodeIds ?? throw new ArgumentNullException(nameof(nodeIds));
        FailedNodeId = failedNodeId;
    }

    /// <summary>The run this truncation belongs to.</summary>
    public string RunId { get; }

    /// <summary>Nodes that will never run, ordered by the breadth-first walk that found them.</summary>
    public IReadOnlyList<string> NodeIds { get; }

    /// <summary>
    /// The node whose failure caused this wave, or <see langword="null"/> when the wave came
    /// from cancelling the run rather than from a failure.
    /// </summary>
    public string? FailedNodeId { get; }
}
