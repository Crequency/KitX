namespace KitX.ToolKit.Contract.View;

/// <summary>
/// The asynchronous face a connected View talks to (ADR-0014 D1/D2).
/// </summary>
/// <remarks>
/// <para><b>Why this exists next to <see cref="IToolkitService"/> rather than replacing it.</b> The
/// management facade is synchronous, returns C# objects and throws; a wire protocol can do none of
/// those. Two shapes, one meaning: every message this channel accepts must correspond to a member on
/// the management side, and the semantic-unit table in the implementation design is the authority on
/// which ones correspond. When they disagree, the table is wrong — not "the network needs something
/// extra" (ADR-0014 D1).</para>
///
/// <para><b>No transport in here.</b> No sockets, no ports, no frames, no serialization format. Those
/// are the implementation's business; this layer knows messages.</para>
///
/// <para><b>Business failure is a message, never an exception</b> (§2). <see cref="SendAsync"/>
/// returns a <see cref="ChannelReply"/> whose failure arm carries a stable code. Only
/// <see cref="OperationCanceledException"/> escapes, because cancellation is not a refusal — it is
/// the caller changing its mind.</para>
/// </remarks>
public interface IViewChannel
{
    /// <summary>
    /// Sends one message and, when it is the kind that has an answer, waits for it.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancels the wait, not the operation on the server.</param>
    /// <returns>
    /// The server's answer. Never <see langword="null"/>; a refusal comes back as
    /// <see cref="ChannelReply.Rejected"/>, not as an exception.
    /// </returns>
    /// <remarks>
    /// <para>Push messages (<c>delta</c>, <c>surface</c>, <c>cleared</c>) have no answer — sending one
    /// from a client is a protocol error the server reports as <c>view.unknown-command</c> on the
    /// next request; this method still returns a reply for the message's own fate.</para>
    ///
    /// <para>Cancelling through <paramref name="cancellationToken"/> does not tell the server to
    /// stop. The server's work is idempotent with respect to the caller losing interest, and a
    /// cancellation that propagated would let a client abort a <c>trigger</c> mid-flight, which the
    /// management facade cannot express either.</para>
    /// </remarks>
    ValueTask<ChannelReply> SendAsync(ViewMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the server pushes a message that answers nothing — <c>delta</c>, <c>surface</c>,
    /// <c>cleared</c>.
    /// </summary>
    /// <remarks>
    /// The split between "returns a reply" and "arrives here" mirrors the protocol: a request has an
    /// answer, a notification does not. Conflating them would force every delta to answer a question
    /// nobody asked.
    /// </remarks>
    event EventHandler<ViewMessage>? Received;
}

/// <summary>What came back for one sent message.</summary>
/// <remarks>
/// Three shapes, matching the wire: a successful acknowledgement, a successful handshake, or a
/// refusal. The handshake is separate from the acknowledgement because a client cannot treat them
/// alike — a <c>welcome</c> replaces its entire state, an <c>acked</c> confirms one operation.
/// </remarks>
public sealed record ChannelReply
{
    private ChannelReply(ViewMessage? answer, string? errorCode, string? errorMessage)
    {
        Answer = answer;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>The server's answer, or <see langword="null"/> when it refused.</summary>
    /// <remarks>
    /// Typed as <see cref="ViewMessage"/> because which answer arrives depends on the request:
    /// <c>connect</c> is answered with <c>welcome</c>, <c>list</c> with <c>toolkits</c>, everything
    /// else with <c>acked</c>.
    /// </remarks>
    public ViewMessage? Answer { get; }

    /// <summary>The stable refusal code, or <see langword="null"/> on success.</summary>
    public string? ErrorCode { get; }

    /// <summary>The human-readable refusal reason, or <see langword="null"/> on success.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Whether the message was accepted.</summary>
    public bool Succeeded => ErrorCode is null;

    /// <summary>The message was accepted.</summary>
    /// <param name="answer">The server's answer. May be <see langword="null"/> for a bare acknowledgement.</param>
    public static ChannelReply Accepted(ViewMessage? answer = null) => new(answer, null, null);

    /// <summary>The message was refused with a stable code.</summary>
    /// <param name="code">One of <see cref="ViewErrorCodes"/>.</param>
    /// <param name="message">A human-readable reason.</param>
    public static ChannelReply Rejected(string code, string message) => new(null, code, message);
}
