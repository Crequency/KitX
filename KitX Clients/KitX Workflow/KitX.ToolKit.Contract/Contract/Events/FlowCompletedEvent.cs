namespace KitX.ToolKit.Contract.Events;

/// <summary>
/// One flow finished. Raised exactly once per flow, whatever ended it — success, failure or
/// cancellation.
/// </summary>
/// <remarks>
/// <para>This is the only event that carries a flow's <em>outcome</em>, and the only one that is
/// flow-granular. It replaces <c>RunCompletedEvent</c>, which despite its name fired once per
/// <em>node</em> and carried a <c>bool Succeeded</c> describing one node's fate (ADR-0015 D2.1).
/// The distinction matters: a five-node flow used to emit five "completions", so no consumer
/// could tell "the flow finished" from "a node finished".</para>
///
/// <para><see cref="Outcome"/> uses the same three-valued enum the rest of the runtime uses, so
/// cancellation is representable and is not squeezed into <c>Succeeded = false</c> (ADR-0005 D2).
/// <see cref="Failure"/> is populated only for <see cref="RunOutcome.Failed"/> and carries a
/// stable code.</para>
/// </remarks>
public sealed record FlowCompletedEvent(
    string EventId,
    string ToolkitId,
    DateTimeOffset Timestamp,
    string FlowId,
    RunOutcome Outcome,
    Diagnostic? Failure) : ToolkitEvent(EventId, ToolkitId, Timestamp);
