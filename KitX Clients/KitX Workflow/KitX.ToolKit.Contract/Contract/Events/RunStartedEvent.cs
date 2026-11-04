namespace KitX.ToolKit.Contract.Events;

/// <summary>A workflow chain started within an instance.</summary>
public sealed record RunStartedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string RunId, string WorkflowId) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
