namespace KitX.ToolKit.Bench;

/// <summary>
/// Raised by <see cref="BenchRunInstance.Completed"/> (surfaced via
/// <see cref="BenchScheduler.RunCompleted"/>) when a triggered run finishes.
/// </summary>
public sealed class BenchRunCompletedEventArgs : EventArgs
{
    public BenchRunCompletedEventArgs(string instanceId, bool isSuccess)
    {
        InstanceId = instanceId;
        IsSuccess = isSuccess;
    }

    /// <summary>The run's unique id.</summary>
    public string InstanceId { get; }

    /// <summary>True when every workflow in the run completed without a node failure.</summary>
    public bool IsSuccess { get; }
}

/// <summary>
/// Raised by <see cref="BenchRunInstance.NodeStarted"/> when a single workflow of a run
/// is scheduled (run-monitor primitive; the instance manager projects it to a
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
/// Raised by <see cref="BenchRunInstance.NodeCompleted"/> when a single workflow of a run
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
