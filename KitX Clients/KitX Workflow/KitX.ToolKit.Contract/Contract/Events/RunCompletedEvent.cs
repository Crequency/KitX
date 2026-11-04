namespace KitX.ToolKit.Contract.Events;

/// <summary>A workflow chain completed within an instance; failure is also completion (Succeeded=false).</summary>
public sealed record RunCompletedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string RunId, string WorkflowId, bool Succeeded, string? Error) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
