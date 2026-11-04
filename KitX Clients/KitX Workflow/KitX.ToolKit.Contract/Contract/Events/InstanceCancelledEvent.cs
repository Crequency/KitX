namespace KitX.ToolKit.Contract.Events;

/// <summary>An instance was ended (cancelled + destroyed) by the user or on unmount.</summary>
public sealed record InstanceCancelledEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp)
    : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
