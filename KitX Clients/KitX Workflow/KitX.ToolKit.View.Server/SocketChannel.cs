using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KitX.ToolKit.Contract.View;
using Serilog;

namespace KitX.ToolKit.View.Server;

/// <summary>
/// The client end of the same envelope <see cref="ViewServer"/> writes: one connected socket becomes
/// an <see cref="IViewChannel"/> by putting the correlation id in a frame instead of in the message.
/// </summary>
/// <remarks>
/// <para><b>Why it lives next to the server rather than next to the renderer.</b> The two are the two
/// ends of one format. A client that computed its own ids and parsed its own replies would be a
/// second implementation of the envelope, and the first thing that would drift is
/// <see cref="ViewWire.Options"/> — which is exactly the defect the seam test found on 2026-11-20.
/// Sharing a project makes the wire's shape impossible to declare twice.</para>
///
/// <para><b>The id is the transport's, not the contract's.</b> Nothing in
/// <see cref="IViewChannel"/> mentions one: the return value of <see cref="SendAsync"/> is the
/// correlation for a caller in the same process, and this class recreates that property across a
/// socket by keeping a table of pending requests. A frame with no id is a push and goes to
/// <see cref="Received"/>; a frame with an id nobody is waiting for is a protocol violation by the
/// peer, and it is logged and dropped rather than raised — raising it would invent a message the
/// protocol never said could arrive this way.</para>
///
/// <para><b>Timeouts are replies, not exceptions.</b> The contract says business failure arrives as
/// <see cref="ChannelReply.Rejected"/> and only <see cref="OperationCanceledException"/> escapes.
/// A server that stops answering is a failure of this request, so it comes back as
/// <see cref="TimeoutCode"/>; a caller that cancels is a caller changing its mind, so that one does
/// escape.</para>
///
/// <para><b>Threading.</b> A receive loop started at construction is the only reader of the socket.
/// Pending requests are keyed by id in a concurrent dictionary, so an answer may arrive on the loop's
/// thread while any number of callers are blocked in <see cref="SendAsync"/>. Sends are serialised by
/// a semaphore because <see cref="ClientWebSocket"/> allows exactly one send in flight.</para>
/// </remarks>
public sealed class SocketChannel : IViewChannel, IDisposable
{
    /// <summary>
    /// The code a request gets back when no answer arrived in time.
    /// </summary>
    /// <remarks>
    /// <b>Local to this class on purpose.</b> <see cref="ViewErrorCodes"/> is a list of codes a
    /// <em>server</em> sends; a timeout is synthesised by the client and never appears on the wire.
    /// Putting it in the contract would claim the server can produce it and invite a server-side
    /// branch that can never run.
    /// </remarks>
    public const string TimeoutCode = "view.timeout";

    /// <summary>The code a request gets back when the connection ended before an answer arrived.</summary>
    public const string DisconnectedCode = "view.disconnected";

    /// <summary>How long a request waits for its answer by default.</summary>
    private static readonly TimeSpan DefaultAnswerPatience = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _answerPatience;
    private readonly ClientWebSocket _socket;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ChannelReply>> _pending = new();

    private readonly CancellationTokenSource _closing = new();
    private readonly Task _reading;

    private int _nextId;
    private bool _disposed;

    /// <summary>Wraps a socket that is already connected.</summary>
    /// <param name="socket">A connected, open <see cref="ClientWebSocket"/>.</param>
    /// <param name="answerTimeout">
    /// How long to wait for an answer. Exposed for tests, because the alternative is a suite that
    /// spends thirty seconds proving that a timeout happens, and a slow test is one people stop
    /// running. Production passes nothing and gets <see cref="DefaultAnswerPatience"/>.
    /// </param>
    public SocketChannel(ClientWebSocket socket, TimeSpan? answerTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _answerPatience = answerTimeout ?? DefaultAnswerPatience;

        // Started here rather than by a Start() method: a channel over an already-open socket has
        // nothing to wait for, and a caller that could forget to start it would get a channel that
        // accepts requests and never answers them.
        _reading = Task.Run(ReadLoopAsync);
    }

    /// <inheritdoc/>
    public event EventHandler<ViewMessage>? Received;

    /// <inheritdoc/>
    public async ValueTask<ChannelReply> SendAsync(
        ViewMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);

        // RunContinuationsAsynchronously: the receive loop completes this source, and the
        // continuation it wakes is the caller's — running it on the reader's thread would stall the
        // reader for as long as the caller's continuation takes.
        var waiter = new TaskCompletionSource<ChannelReply>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(id, waiter))
            throw new InvalidOperationException($"the id '{id}' is already waiting; the counter wrapped");

        try
        {
            await SendFrameAsync(id, message, cancellationToken);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            _pending.TryRemove(id, out _);

            // A socket that is gone is the same answer as a socket that never replied: this request
            // did not succeed. Letting it escape would contradict the interface, which promises that
            // a failure to do the thing arrives as ChannelReply.Rejected and only cancellation
            // escapes — a caller with no catch block would see a crashed card instead of a message.
            return ChannelReply.Rejected(DisconnectedCode, FirstSentenceOf(ex.Message));
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        // WaitAsync, not WhenAny: a cancellation has to propagate as OperationCanceledException,
        // which is the one exception the interface documents as escaping.
        try
        {
            return await waiter.Task.WaitAsync(_answerPatience, cancellationToken);
        }
        catch (TimeoutException)
        {
            _pending.TryRemove(id, out _);

            // The answer may still arrive later; the receive loop will find no waiter and say so.
            return ChannelReply.Rejected(
                TimeoutCode,
                $"no answer to '{message.GetType().Name}' within {_answerPatience.TotalSeconds:0.###} seconds");
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    private async Task SendFrameAsync(string id, ViewMessage message, CancellationToken cancellationToken)
    {
        var text = JsonSerializer.Serialize(new ViewFrame(id, message), ViewWire.Options);
        var bytes = Encoding.UTF8.GetBytes(text);

        // One send at a time: ClientWebSocket rejects overlapping sends with InvalidOperationException,
        // and two controls raising at once is an ordinary thing for a card to do.
        await _sendGate.WaitAsync(cancellationToken);

        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[16 * 1024];
        var accumulated = new MemoryStream();

        try
        {
            while (!_closing.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer, _closing.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                accumulated.Write(buffer, 0, result.Count);

                if (!result.EndOfMessage)
                    continue;

                var text = Encoding.UTF8.GetString(accumulated.GetBuffer(), 0, (int)accumulated.Length);
                accumulated.SetLength(0);

                Accept(text);
            }
        }
        catch (OperationCanceledException)
        {
            // Our own Dispose, which is the normal way out.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[SocketChannel] The connection failed while reading");
        }
        finally
        {
            FailEverythingWaiting(DisconnectedCode, "the connection ended before an answer arrived");
        }
    }

    private void Accept(string text)
    {
        ViewFrame? frame;

        try
        {
            frame = JsonSerializer.Deserialize<ViewFrame>(text, ViewWire.Options);
        }
        catch (JsonException ex)
        {
            // The server writes this shape; anything else is a peer we cannot talk to. Dropping the
            // frame keeps the connection alive, which is the same choice ViewServer makes in the
            // other direction.
            Log.Warning("[SocketChannel] Unreadable frame: {Reason}", ex.Message);
            return;
        }

        if (frame is null)
            return;

        if (frame.Id is null)
        {
            // A push answers nothing, so nothing is waiting for it.
            Received?.Invoke(this, frame.Message);
            return;
        }

        if (!_pending.TryRemove(frame.Id, out var waiter))
        {
            // A reply to something nobody asked for, or a reply that arrived after its timeout.
            // Dropping it is the honest response: there is no request it belongs to.
            Log.Warning(
                "[SocketChannel] An answer arrived for id {Id} with nobody waiting: {Type}",
                frame.Id, frame.Message.GetType().Name);
            return;
        }

        waiter.TrySetResult(ToReply(frame.Message));
    }

    /// <summary>Turns the server's answer frame back into what the interface returns.</summary>
    /// <remarks>
    /// <b><c>acked</c> becomes <c>Accepted(null)</c> on purpose.</b> A bare acknowledgement means the
    /// operation succeeded and said nothing; handing the caller an <see cref="AckedReply"/> object
    /// would invite a branch on a message that carries no information the request did not already
    /// imply.
    /// </remarks>
    private static ChannelReply ToReply(ViewMessage message) => message switch
    {
        RejectedReply rejected => ChannelReply.Rejected(rejected.Code, rejected.Message),
        AckedReply => ChannelReply.Accepted(),
        _ => ChannelReply.Accepted(message),
    };

    /// <summary>The first line of a message, for a status line that has one row to work with.</summary>
    private static string FirstSentenceOf(string message)
    {
        var cut = message.IndexOfAny(['\r', '\n']);

        return cut < 0 ? message : message[..cut];
    }

    private void FailEverythingWaiting(string code, string message)
    {
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var waiter))
                waiter.TrySetResult(ChannelReply.Rejected(code, message));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Cancel first: the receive loop is blocked in ReceiveAsync and closing the socket
        // underneath it is the documented way to wake it, but cancelling is the polite one and the
        // socket close below follows either way.
        try { _closing.Cancel(); }
        catch (ObjectDisposedException) { }

        FailEverythingWaiting(DisconnectedCode, "the channel was closed");

        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None)
                    .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // A socket that will not close is already broken; the point of Dispose is the local
            // resources, and the caller cannot act on this.
            Log.Debug(ex, "[SocketChannel] Close handshake failed");
        }

        _socket.Dispose();
        _closing.Dispose();

        // Not awaited: Dispose runs on a caller's thread, and the loop exits on the cancellation
        // above. Observed so an exception inside it cannot become an unobserved task exception.
        _ = _reading.ContinueWith(
            t => Log.Debug(t.Exception, "[SocketChannel] Reader ended with a fault"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
