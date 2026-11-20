using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Fleck;
using KitX.ToolKit.Contract.View;
using Serilog;

namespace KitX.ToolKit.View.Server;

/// <summary>
/// A WebSocket listener that turns each connection into one <see cref="IViewChannel"/> and pumps
/// frames between them.
/// </summary>
/// <remarks>
/// <para><b>One connection, one channel.</b> The channel owns a session, an attachment and an
/// event subscription into the <c>DataStore</c>. Sharing one between two sockets would deliver one
/// client's deltas to the other and make the second client's <c>connect</c> silently move the
/// first one's ToolKit — that is why <c>AddKitXToolKit</c> registers
/// <see cref="Func{TResult}"/> rather than the channel itself (<c>ServiceCollectionExtensions.cs</c>
/// line 144ff), and this class is the consumer that registration was waiting for: a channel is
/// created at open and disposed at close.</para>
///
/// <para><b>It knows nothing about ToolKits.</b> This type may reference only
/// <c>KitX.ToolKit.Contract</c>; a transport that could name <c>DataStore</c> or the instance
/// manager would be able to answer a question the protocol never asked. The assertion is in
/// <c>ViewServerBoundaryTests</c>, reading the compiled metadata rather than this paragraph.</para>
///
/// <para><b>Failures are frames.</b> A refusal from the channel already arrives as
/// <see cref="ChannelReply.Rejected"/>, so it becomes a <c>rejected</c> message with the same id.
/// A frame that cannot even be parsed cannot be answered with its own id — it has none we can
/// trust — so it is answered with a null id and the connection stays open: a client that sent
/// garbage once may send something valid next, and dropping the socket turns a typo into a
/// reconnect.</para>
///
/// <para><b>Threading.</b> Fleck delivers <c>OnMessage</c> on its own receive loop. Everything this
/// class answers is serialised per connection by a lock, because a push raised by the store can
/// arrive on any thread and both end up calling <c>SendAsync</c> on the same socket.</para>
/// </remarks>
public sealed class ViewServer : IDisposable
{
    private readonly Func<IViewChannel> _newChannel;
    private readonly WebSocketServer _server;

    private readonly List<Connection> _connections = [];
    private readonly Lock _gate = new();

    private bool _disposed;

    /// <summary>Starts listening.</summary>
    /// <param name="newChannel">
    /// Creates the channel for one connection. Called once per accepted socket, and the result is
    /// disposed when that socket closes.
    /// </param>
    /// <param name="port">
    /// The port to listen on, or 0 to let the operating system choose. When 0 is passed,
    /// <see cref="Port"/> carries the port that was actually bound — which is the only way a test
    /// can connect without reserving a number and racing for it.
    /// </param>
    /// <param name="address">The interface to bind. Loopback by default.</param>
    public ViewServer(Func<IViewChannel> newChannel, int port = 0, string address = "127.0.0.1")
    {
        ArgumentNullException.ThrowIfNull(newChannel);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        _newChannel = newChannel;

        // Fleck parses the location eagerly and throws FormatException on a host that is not an
        // IP literal, so a hostname here would be a constructor-time failure rather than a listen
        // failure. Kept as a plain address string for that reason.
        _server = new WebSocketServer($"ws://{address}:{port}");

        // Fleck logs to the console by default, which would put its framing chatter into every
        // test run and every host's stdout. Its errors still surface through OnError on the
        // connection, so this only silences the layer that says "sent 37 bytes".
        FleckLog.Level = LogLevel.Warn;
    }

    /// <summary>The port actually bound. Meaningful after <see cref="Start"/>.</summary>
    public int Port => _server.Port;

    /// <summary>Begins accepting connections.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _server.Start(socket => OnConnected(socket));

        Log.Information("[ViewServer] Listening on ws://127.0.0.1:{Port}", Port);
    }

    private void OnConnected(IWebSocketConnection socket)
    {
        IViewChannel channel;

        try
        {
            channel = _newChannel();
        }
        catch (Exception ex)
        {
            // A container that cannot build a channel is not a per-connection problem, but the
            // socket is already accepted: closing it without a word would leave the client with a
            // connection that opened and then died for no stated reason.
            Log.Error(ex, "[ViewServer] Could not create a channel for a new connection");
            socket.Close();
            return;
        }

        var connection = new Connection(socket, channel);

        lock (_gate)
            _connections.Add(connection);

        socket.OnOpen = connection.Open;
        socket.OnClose = () => Closed(connection);
        socket.OnError = ex => Log.Warning(ex, "[ViewServer] Connection error");
        socket.OnMessage = text => connection.Receive(text);
    }

    private void Closed(Connection connection)
    {
        lock (_gate)
            _connections.Remove(connection);

        connection.Dispose();
    }

    /// <summary>Stops listening and closes every open connection.</summary>
    public void Stop()
    {
        List<Connection> open;

        lock (_gate)
        {
            open = [.. _connections];
            _connections.Clear();
        }

        foreach (var connection in open)
        {
            connection.Dispose();
            connection.Close();
        }

        _server.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }

    /// <summary>One socket and the channel behind it.</summary>
    private sealed class Connection : IDisposable
    {
        private readonly IWebSocketConnection _socket;
        private readonly IViewChannel _channel;
        private readonly Lock _sendGate = new();

        private bool _disposed;

        public Connection(IWebSocketConnection socket, IViewChannel channel)
        {
            _socket = socket;
            _channel = channel;

            _channel.Received += OnChannelReceived;
        }

        public void Open() => Log.Debug("[ViewServer] Client connected");

        public void Close()
        {
            if (_socket.IsAvailable)
                _socket.Close();
        }

        /// <summary>Reads one frame and answers it, if it asks for an answer.</summary>
        public void Receive(string text)
        {
            ViewFrame? frame;

            try
            {
                frame = JsonSerializer.Deserialize<ViewFrame>(text, ViewWire.Options);
            }
            catch (JsonException ex)
            {
                // No id: a frame we cannot parse is a frame whose id we cannot trust. Answering
                // with a null id still tells the client something happened, which is more than
                // silence does.
                Log.Information("[ViewServer] Unreadable frame: {Reason}", ex.Message);
                Send(null, new RejectedReply
                {
                    Code = ViewErrorCodes.Malformed,
                    Message = ex.Message,
                });
                return;
            }

            if (frame is null)
            {
                Send(null, new RejectedReply
                {
                    Code = ViewErrorCodes.Malformed,
                    Message = "The frame was empty.",
                });
                return;
            }

            _ = AnswerAsync(frame);
        }

        private async Task AnswerAsync(ViewFrame frame)
        {
            ChannelReply reply;

            try
            {
                reply = await _channel.SendAsync(frame.Message, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Documented as the one thing that escapes the channel, and it means the caller
                // changed its mind — there is nothing to report back to a caller that is gone.
                return;
            }
            catch (Exception ex)
            {
                // The channel is supposed to turn every refusal into a reply; anything that
                // escapes here is a server fault. It still has an id, so the client can tell its
                // own request failed rather than guessing.
                Log.Error(ex, "[ViewServer] The channel threw while handling {Type}",
                    frame.Message.GetType().Name);

                Send(frame.Id, new RejectedReply
                {
                    Code = ViewErrorCodes.Failed,
                    Message = ex.Message,
                });
                return;
            }

            if (!reply.Succeeded)
            {
                Send(frame.Id, new RejectedReply
                {
                    Code = reply.ErrorCode!,
                    Message = reply.ErrorMessage ?? reply.ErrorCode!,
                });
                return;
            }

            if (reply.Answer is not null)
                Send(frame.Id, reply.Answer);
            else
                Send(frame.Id, new AckedReply());
        }

        private void OnChannelReceived(object? sender, ViewMessage message) => Send(null, message);

        private void Send(string? id, ViewMessage message)
        {
            if (_disposed || !_socket.IsAvailable)
                return;

            var text = JsonSerializer.Serialize(new ViewFrame(id, message), ViewWire.Options);

            // Per connection, because a push from the store thread and an answer from the receive
            // loop can be in flight at once, and Fleck's send writes to the socket directly.
            lock (_sendGate)
            {
                _ = _socket.Send(text).ContinueWith(
                    t => Log.Debug(t.Exception, "[ViewServer] Send failed"),
                    TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _channel.Received -= OnChannelReceived;

            // IViewChannel carries no Dispose, so this is a cast rather than a call. That is worth
            // stating instead of hiding: the contract describes a channel as a pair of members
            // (SentAsync's answer and the Received event) and deliberately says nothing about
            // lifetime, because a channel that could only be used once would make the
            // "one per connection" rule a matter of discipline rather than of the type. The one
            // implementation in this repository (ViewChannel) is IDisposable; a future one that is
            // not simply has nothing to release, which is why the cast is an `is` and not a throw.
            if (_channel is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
