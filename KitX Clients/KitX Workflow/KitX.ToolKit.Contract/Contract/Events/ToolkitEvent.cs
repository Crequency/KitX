namespace KitX.ToolKit.Contract.Events;

/// <summary>
/// Base for every event the runtime raises. Serializable records, so the same DTOs serve the
/// in-process desktop adapter and the networked View channel (ADR-0014).
/// </summary>
/// <remarks>
/// <para><b>No instance identity here.</b> Until 2026-11-08 this carried a required
/// <c>InstanceId</c>, which forced every event to name an instance even when none existed — the
/// lifecycle events filled it with an empty string, and the run events filled it with whatever
/// instance happened to be hosting. Q83 collapsed "instance" into "the toolkit's execution
/// context", so the field was answering a question the service can no longer ask.</para>
///
/// <para>What is left is what an event actually needs: which event, which toolkit, when. Events
/// that belong to a single flow add <c>FlowId</c> themselves (ADR-0015 D1).</para>
/// </remarks>
public abstract record ToolkitEvent(string EventId, string ToolkitId, DateTimeOffset Timestamp);
