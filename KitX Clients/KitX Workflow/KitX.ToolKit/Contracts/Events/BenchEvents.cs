using System.Text.Json;

namespace KitX.ToolKit.Contracts.Events;

/// <summary>
/// Base for all Bench events. Serializable records so the same DTOs serve the in-process
/// desktop adapter and (later) the remote HTTP/WS bridge (M5). Every event carries the
/// <see cref="InstanceId"/> it belongs to, so subscribers can filter by instance.
/// </summary>
public abstract record BenchEvent(string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp);

/// <summary>An instance was spawned (a Spawn trigger fired).</summary>
public sealed record InstanceSpawnedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string TriggerId, Initiator Initiator, JsonElement Payload) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>An instance transitioned to Completed (all chains finished).</summary>
public sealed record InstanceCompletedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    bool Succeeded) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>An instance was ended (cancelled + destroyed) by the user or on unmount.</summary>
public sealed record InstanceCancelledEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp)
    : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>A workflow chain started within an instance.</summary>
public sealed record RunStartedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string RunId, string WorkflowId) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>A workflow chain completed within an instance; failure is also completion (Succeeded=false).</summary>
public sealed record RunCompletedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string RunId, string WorkflowId, bool Succeeded, string? Error) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>A DataStore key changed (Set/Remove/Append) — the panel projection + remote push primitive.</summary>
public sealed record DataStoreChangedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string Key, JsonElement? OldValue, JsonElement? NewValue, bool Removed) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>A panel control's state changed (SetControlValue / UiSet derived).</summary>
public sealed record UiControlStateChangedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string ControlId, string Prop, JsonElement? Value) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>A log entry was appended (UiLog derived).</summary>
public sealed record UiLogEntryEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string ControlId, object? Entry) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>A progress value updated (UiProgress derived).</summary>
public sealed record UiProgressUpdateEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string ControlId, double Value) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>A dialog was requested (UiDialog wrote the request slot).</summary>
public sealed record DialogRequestedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp,
    string ControlId, string Message, IReadOnlyList<string> Buttons) : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);

/// <summary>The host should present (open/focus) an instance's panel.</summary>
public sealed record PanelOpenRequestedEvent(
    string EventId, string ToolkitId, string InstanceId, DateTimeOffset Timestamp)
    : BenchEvent(EventId, ToolkitId, InstanceId, Timestamp);
