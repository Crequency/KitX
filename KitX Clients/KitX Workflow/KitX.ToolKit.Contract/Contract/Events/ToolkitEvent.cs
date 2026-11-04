namespace KitX.ToolKit.Contract.Events;

/// <summary>
/// Base for all Bench events. Serializable records so the same DTOs serve the in-process
/// desktop adapter and (later) the remote HTTP/WS bridge (M5). Every event carries the
/// <see cref="InstanceId"/> it belongs to, so subscribers can filter by instance.
/// </summary>
public abstract record ToolkitEvent(string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp);
