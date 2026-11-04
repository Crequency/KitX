namespace KitX.ToolKit.Contract.Events;

/// <summary>An instance transitioned to Completed (all chains finished).</summary>
public sealed record InstanceCompletedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    bool Succeeded) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
