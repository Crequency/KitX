namespace KitX.ToolKit.Bench;

/// <summary>
/// Raised by <see cref="Run.WorkflowRun.Completed"/> (surfaced via
/// <see cref="BenchScheduler.RunCompleted"/>) when a triggered run settles.
/// </summary>
/// <remarks>
/// The verdict is a <see cref="Contract.RunOutcome"/> rather than a bool because "cancelled"
/// and "failed" are different answers (ADR-0009 acceptance #2). A bool would force every
/// caller to decide privately which one cancellation maps to — and they would disagree.
/// </remarks>
public sealed class BenchRunCompletedEventArgs : EventArgs
{
    public BenchRunCompletedEventArgs(
        string instanceId,
        Contract.RunOutcome outcome,
        int failedCount = 0,
        int completedCount = 0,
        int skippedCount = 0,
        Contract.Diagnostic? failure = null)
    {
        InstanceId = instanceId;
        Outcome = outcome;
        FailedCount = failedCount;
        CompletedCount = completedCount;
        SkippedCount = skippedCount;
        Failure = failure;
    }

    /// <summary>The run's unique id.</summary>
    public string InstanceId { get; }

    /// <summary>How the run ended: succeeded, failed, or cancelled.</summary>
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
/// executing (run-monitor primitive; the instance manager projects it to a
/// <c>RunStartedEvent</c> on the Bench contract).
/// </summary>
public sealed class BenchNodeStartedEventArgs : EventArgs
{
    public BenchNodeStartedEventArgs(string instanceId, string workflowId)
    {
        InstanceId = instanceId;
        WorkflowId = workflowId;
    }

    /// <summary>The run's unique id (not the owning instance namespace).</summary>
    public string InstanceId { get; }

    /// <summary>The workflow that started.</summary>
    public string WorkflowId { get; }
}

/// <summary>
/// Raised by <see cref="Run.WorkflowRun.NodeCompleted"/> when a single workflow of a run
/// finishes (run-monitor primitive; the instance manager projects it to a
/// <c>RunCompletedEvent</c> on the Bench contract).
/// </summary>
public sealed class BenchNodeCompletedEventArgs : EventArgs
{
    public BenchNodeCompletedEventArgs(string instanceId, string workflowId, bool succeeded, string? error)
    {
        InstanceId = instanceId;
        WorkflowId = workflowId;
        Succeeded = succeeded;
        Error = error;
    }

    /// <summary>The run's unique id (not the owning instance namespace).</summary>
    public string InstanceId { get; }

    /// <summary>The workflow that finished.</summary>
    public string WorkflowId { get; }

    /// <summary>True when this workflow finished successfully.</summary>
    public bool Succeeded { get; }

    /// <summary>Failure detail, when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; }
}
