namespace KitX.ToolKit.Contract.Events;

/// <summary>The host should present (open/focus) an instance's panel.</summary>
public sealed record PanelOpenRequestedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp)
    : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
