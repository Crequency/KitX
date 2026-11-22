using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// The remote half of what the window does: the same four operations as <see cref="HostSession"/>,
/// against a channel it cannot reach behind.
/// </summary>
/// <remarks>
/// <para><b>Why this is a second class and not a nullable <see cref="HostSession"/>.</b>
/// <see cref="HostSession"/> owns a container, so it also exposes <c>Toolkits</c>,
/// <c>Surfaces</c>, <c>WorkflowFiles</c> and <c>Keys</c> — the server's own objects. A remote client
/// has none of them, and "the property exists but is null in remote mode" would turn four
/// compile-time facts into four comments. ADR-0014 D5 exists so that a View cannot reach the
/// management side; a class whose remote constructor leaves four holes is that rule honoured in
/// prose.</para>
///
/// <para><b>What is deliberately absent, and why each absence is a decision.</b></para>
/// <list type="bullet">
/// <item><b>No <c>EnsureSampleAsync</c>, <c>SeedSampleSurface</c>, <c>SeedSampleValues</c>.</b>
/// Seeding is the server's business: a client that could write a card's layout would be a client
/// that could change what everybody sees. Those three exist in <see cref="HostSession"/> because it
/// <em>is</em> the server for the sample.</item>
/// <item><b>No <c>Root</c>.</b> There is no storage root on this side; the only thing a remote
/// client knows is the address it connected to.</item>
/// <item><b>No mounting.</b> <c>mount</c> is not a wire command (removed 2026-11-11), so a remote
/// client lists what is already mounted and can do nothing about it.</item>
/// </list>
///
/// <para><b>It still owns no control.</b> The same rule as <see cref="HostSession"/>: a push arrives
/// on a socket's receive thread, and an Avalonia control touched from there throws. Subscribers to
/// <see cref="MessageApplied"/> marshal themselves.</para>
/// </remarks>
public sealed class RemoteSession : IDisposable
{
    private readonly IViewChannel _channel;

    private bool _disposed;

    /// <summary>Wraps one connected channel.</summary>
    /// <param name="channel">
    /// The connection. The session subscribes to it and disposes it — a caller that hands a channel
    /// over has nothing left to do with it, and a session that left it open would leak a socket per
    /// window.
    /// </param>
    public RemoteSession(IViewChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        _channel.Received += OnReceived;
    }

    /// <summary>The client-side projection folded from what the server said.</summary>
    public ToolkitView View { get; } = new();

    /// <summary>The ToolKit this session is attached to, or <see langword="null"/>.</summary>
    public string? AttachedToolkitId { get; private set; }

    /// <summary>Raised after every server→client message was folded into the projection.</summary>
    /// <remarks>
    /// On the channel's receive thread. See the class remarks: subscribers that own controls must
    /// marshal.
    /// </remarks>
    public event EventHandler<ViewMessage>? MessageApplied;

    /// <summary>Asks the server which ToolKits a View may attach to.</summary>
    /// <returns>The rows, or a refusal's message.</returns>
    public async Task<(IReadOnlyList<ToolkitSummary> Rows, string? Error)> ListAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var reply = await _channel.SendAsync(new ListMessage());

        if (!reply.Succeeded)
            return ([], reply.ErrorMessage ?? reply.ErrorCode);

        return (Narrow<ToolkitsMessage>(reply.Answer).Toolkits, null);
    }

    /// <summary>Attaches to one ToolKit and folds the handshake into the projection.</summary>
    /// <returns><see langword="null"/> on success, otherwise the refusal's message.</returns>
    public async Task<string?> ConnectAsync(string toolkitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var reply = await _channel.SendAsync(new ConnectMessage(toolkitId));

        if (!reply.Succeeded)
            return reply.ErrorMessage ?? reply.ErrorCode;

        AttachedToolkitId = toolkitId;
        Apply(Narrow<WelcomeMessage>(reply.Answer));

        return null;
    }

    /// <summary>Sends one command and reports a refusal.</summary>
    public async Task<string?> SendAsync(CommandMessage command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var reply = await _channel.SendAsync(command);

        LastAck = reply.Answer as AckedReply;

        return reply.Succeeded ? null : reply.ErrorMessage ?? reply.ErrorCode;
    }

    /// <summary>The acknowledgement the last command came back with, if it carried one.</summary>
    /// <remarks>
    /// <para><b>Why the session remembers this instead of returning it.</b> <see cref="SendAsync"/>
    /// returns a refusal or <see langword="null"/> because every caller so far only asked "did it
    /// work". A remote client can ask one more question — "did a trigger answer to that name" —
    /// and the answer is in the ack's <c>started</c> value, which exists nowhere else. Returning a
    /// reply from <see cref="SendAsync"/> would make every existing caller write
    /// <c>_ =</c> in front of it.</para>
    ///
    /// <para><b>It is the last ack, not a queue.</b> A caller that needs the ack of a particular
    /// command should send that command and read this immediately, which is what one window with one
    /// user does.</para>
    /// </remarks>
    public AckedReply? LastAck { get; private set; }

    /// <summary>The card this session is looking at, as the server last described it.</summary>
    /// <remarks>
    /// Exposed so a caller does not have to keep its own copy in step with the projection. It is
    /// the same document <see cref="View"/> holds; naming it here is for readability at call sites
    /// that only care about the layout.
    /// </remarks>
    public SurfaceDocument Surface => View.Surface;

    private void OnReceived(object? sender, ViewMessage message) => Apply(message);

    private void Apply(ViewMessage message)
    {
        View.Apply(message);

        MessageApplied?.Invoke(this, message);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _channel.Received -= OnReceived;

        (_channel as IDisposable)?.Dispose();
    }

    /// <summary>Narrows a reply to the type the request is documented to answer with.</summary>
    private static T Narrow<T>(ViewMessage? answer) where T : ViewMessage =>
        answer as T ?? throw new InvalidOperationException(
            $"expected {typeof(T).Name}, got {answer?.GetType().Name ?? "nothing"}");
}
