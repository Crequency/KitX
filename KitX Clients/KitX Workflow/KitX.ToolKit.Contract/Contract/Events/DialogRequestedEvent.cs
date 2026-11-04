namespace KitX.ToolKit.Contract.Events;

/// <summary>A dialog was requested (UiDialog wrote the request slot).</summary>
public sealed record DialogRequestedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string ControlId, string Message, IReadOnlyList<string> Buttons) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
