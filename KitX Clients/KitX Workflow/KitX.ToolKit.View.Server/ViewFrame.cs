namespace KitX.ToolKit.View.Server;

/// <summary>
/// One frame on the wire: a message, and — when the frame expects an answer — the id that ties the
/// answer to the question.
/// </summary>
/// <remarks>
/// <para><b>Why the id is here and not on <c>ViewMessage</c>.</b> The design document (01 §5.9.5)
/// used to draw <c>→ { type: "command", requestId, ... }</c>, and the contract never had such a
/// field — <c>git log -S requestId</c> over <c>KitX.ToolKit.Contract/View</c> finds no commit that
/// ever added one. That gap was not an unfinished implementation, it was the contract being right
/// by accident: <see cref="Contract.View.IViewChannel.SendAsync"/> returns a
/// <c>ValueTask&lt;ChannelReply&gt;</c>, and that return value <em>is</em> the correlation. A process
/// that calls the channel directly has nothing to correlate and needs no id. An id only becomes
/// necessary once there is a socket, and on a socket the id belongs to the envelope — which is what
/// this type is.</para>
///
/// <para><b>So the rule is: requests carry an id, pushes do not.</b> A <c>delta</c>, a
/// <c>surface</c> or a <c>cleared</c> was not asked for, so it answers nothing; inventing an id for
/// it would invite a client to try to match it to a request it never made.</para>
/// </remarks>
/// <param name="Id">The correlation id, or <see langword="null"/> for a push.</param>
/// <param name="Message">The message, in the shape the contract already defines.</param>
public sealed record ViewFrame(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] string? Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] Contract.View.ViewMessage Message)
{
    // There was an `ExpectsAnswer => Id is not null` here until 2026-11-20. Nothing consumed it:
    // the only code that decides between an answer and an `acked` asks `reply.Answer is not null`,
    // because the *channel* is what knows whether a request has an answer, and the envelope only
    // carries the id back. It described a design that has not happened — deciding the reply from the
    // envelope — and a property nobody reads is worse than no property, because the next reader
    // assumes something is consulting it. The rule it was documenting (requests carry an id, pushes
    // do not) is stated above, where the record is declared.
}
