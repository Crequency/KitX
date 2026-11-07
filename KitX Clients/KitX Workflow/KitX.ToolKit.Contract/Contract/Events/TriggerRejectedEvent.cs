namespace KitX.ToolKit.Contract.Events;

/// <summary>
/// A trigger fired but was not allowed to start a flow. <see cref="Code"/> is a stable
/// machine-readable reason; hosts branch on it and never parse the message.
/// </summary>
/// <remarks>
/// <para>Replaces the S0-era <c>InstanceSpawnRejectedEvent</c>. The old name assumed the
/// rejection happened while spawning an instance, which was only ever true of one of the two
/// call sites — the other rejects an event-driven chain that creates no instance at all. A
/// rejection is a fact about the <em>trigger</em>, not about an instance, so the event says so.</para>
///
/// <para>Known codes: <c>concurrency-limit</c> (machine-wide flow budget exhausted) and
/// <c>toolkit.not-enabled</c> (the ToolKit is mounted but disabled).</para>
/// </remarks>
public sealed record TriggerRejectedEvent(
    string EventId,
    string ToolkitId,
    DateTimeOffset Timestamp,
    string TriggerId,
    string Code) : ToolkitEvent(EventId, ToolkitId, Timestamp);
