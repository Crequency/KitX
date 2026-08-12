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
