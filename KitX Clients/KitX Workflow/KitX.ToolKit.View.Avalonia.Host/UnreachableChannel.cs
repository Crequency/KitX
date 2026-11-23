using KitX.ToolKit.Contract.View;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// A channel for a port nobody is listening on: every request is refused, locally.
/// </summary>
/// <remarks>
/// <para><b>Why a window opens at all when the connection failed.</b> The alternative is to exit
/// before any window exists, and the person sees a program that started and stopped — which is
/// indistinguishable from a crash, a missing runtime, or a bad argument. A window that opens with a
/// sentence naming the address is a different experience: it says the port was reached for and
/// nobody answered.</para>
///
/// <para><b>The refusal is a reply, not an exception.</b> That is the protocol's own rule (§2: a
/// business failure is a message), and it is also what makes this type small — the window's existing
/// error paths already know how to display a refused <c>list</c>. Nothing here invents a second way
/// to report failure.</para>
///
/// <para><b>It is not a retry queue.</b> There is no reconnect, and that is a scope decision rather
/// than a limitation: a session that silently reconnected would make "it worked the second time"
/// impossible to distinguish from "the first failure was never real".</para>
/// </remarks>
internal sealed class UnreachableChannel : IViewChannel
{
    private readonly string _reason;

    /// <summary>Refuses everything, quoting <paramref name="reason"/>.</summary>
    internal UnreachableChannel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        _reason = reason;
    }

    /// <summary>Never raised: this channel has no server to push anything.</summary>
    public event EventHandler<ViewMessage>? Received
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public ValueTask<ChannelReply> SendAsync(ViewMessage message, CancellationToken cancellationToken = default)
        => new(ChannelReply.Rejected(ViewErrorCodes.NotAttached, _reason));
}
