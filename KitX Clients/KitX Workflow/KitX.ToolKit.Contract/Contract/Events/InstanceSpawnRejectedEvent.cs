namespace KitX.ToolKit.Contract.Events;

/// <summary>
/// A spawn request was rejected before an instance existed (e.g. MaxInstances cap reached).
/// No instance id is assigned; the panel host surfaces <see cref="Reason"/> instead of
/// selecting or clearing a tree item.
/// </summary>
public sealed record InstanceSpawnRejectedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string TriggerId, string Reason) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
