namespace KitX.ToolKit.Run;

/// <summary>
/// Raised by <see cref="Run.WorkflowRun.Completed"/> (surfaced via
/// <see cref="RunScheduler.RunCompleted"/>) when a triggered run settles.
/// </summary>
/// <remarks>
/// <para><b>A run is a flow.</b> The two names are the same thing at different layers: the
/// scheduler calls it a run because it owns the scheduling, the contract calls it a flow because
/// that is what a View author sees. After Q83 there is nothing left to call an "instance", so
/// <see cref="FlowId"/> replaces the old <c>InstanceId</c> — which was always this value
/// (see <c>WorkflowRun.cs</c>'s <c>RunId</c>), just misnamed.</para>
///
/// <para>The verdict is a <see cref="Contract.RunOutcome"/> rather than a bool because "cancelled"
/// and "failed" are different answers (ADR-0009 acceptance #2). A bool would force every caller to
/// decide privately which one cancellation maps to — and they would disagree.</para>
/// </remarks>
internal sealed class RunCompletedEventArgs : EventArgs
{
    public RunCompletedEventArgs(
        string flowId,
        Contract.RunOutcome outcome,
        int failedCount = 0,
        int completedCount = 0,
        int skippedCount = 0,
        Contract.Diagnostic? failure = null)
    {
        FlowId = flowId;
        Outcome = outcome;
        FailedCount = failedCount;
        CompletedCount = completedCount;
        SkippedCount = skippedCount;
        Failure = failure;
    }

    /// <summary>The flow this outcome belongs to.</summary>
    public string FlowId { get; }

    /// <summary>How the flow ended: succeeded, failed, or cancelled.</summary>
    public Contract.RunOutcome Outcome { get; }

    /// <summary>How many nodes ran and failed.</summary>
    public int FailedCount { get; }

    /// <summary>How many nodes settled after running (completed + failed + cancelled).</summary>
    public int CompletedCount { get; }

    /// <summary>How many nodes never ran because the run was truncated.</summary>
    public int SkippedCount { get; }

    /// <summary>
    /// Why the run failed, when <see cref="Outcome"/> is <see cref="Contract.RunOutcome.Failed"/>;
    /// otherwise null. Carries a stable <see cref="Contract.Diagnostic.Code"/> so a consumer can
    /// branch without matching on the message (ADR-0005 D4, ADR-0007 D1).
    /// </summary>
    public Contract.Diagnostic? Failure { get; }

    /// <summary>True only when every node that ran succeeded and nothing was skipped.</summary>
    public bool IsSuccess => Outcome == Contract.RunOutcome.Succeeded && SkippedCount == 0;
}

/// <summary>
/// Raised by <see cref="Run.WorkflowRun.NodeStarted"/> when a single workflow of a run begins
/// executing.
/// </summary>
/// <remarks>
/// Node-granular, and deliberately has no contract event counterpart: ADR-0015 D2.1 removed the
/// node-level events because their only consumer was a panel that lives in <c>_archived/</c>.
/// These stay internal for the run's own scheduling bookkeeping.
/// </remarks>
internal sealed class NodeStartedEventArgs : EventArgs
{
    public NodeStartedEventArgs(string flowId, string workflowId)
    {
        FlowId = flowId;
        WorkflowId = workflowId;
    }

    /// <summary>The flow this node belongs to.</summary>
    public string FlowId { get; }

    /// <summary>The workflow that started.</summary>
    public string WorkflowId { get; }
}

/// <summary>
/// Raised by <see cref="Run.WorkflowRun.NodeCompleted"/> when a single workflow of a run finishes.
/// </summary>
/// <remarks>
/// Node-granular like <see cref="NodeStartedEventArgs"/>. <see cref="Succeeded"/> describes this
/// one node, not the flow — which is exactly why the old contract-level projection of this event
/// was wrong.
/// </remarks>
internal sealed class NodeCompletedEventArgs : EventArgs
{
    public NodeCompletedEventArgs(string flowId, string workflowId, bool succeeded, string? error)
    {
        FlowId = flowId;
        WorkflowId = workflowId;
        Succeeded = succeeded;
        Error = error;
    }

    /// <summary>The flow this node belongs to.</summary>
    public string FlowId { get; }

    /// <summary>The workflow that finished.</summary>
    public string WorkflowId { get; }

    /// <summary>True when this workflow finished successfully.</summary>
    public bool Succeeded { get; }

    /// <summary>Failure detail, when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; }
}
