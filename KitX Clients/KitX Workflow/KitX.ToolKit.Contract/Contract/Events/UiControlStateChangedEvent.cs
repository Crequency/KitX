using System.Text.Json;

namespace KitX.ToolKit.Contract.Events;

/// <summary>A panel control's state changed (SetControlValue / UiSet derived).</summary>
public sealed record UiControlStateChangedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string ControlId, string Prop, JsonElement? Value) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
