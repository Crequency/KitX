using System.Net.WebSockets;
using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Hosting;
using KitX.ToolKit.Storage;
using KitX.ToolKit.View.Server;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// <see cref="SocketChannel"/> against a real <see cref="ViewServer"/>, in one process but over a
/// real socket: the client half of the envelope, exercised by the thing that writes the other half.
/// </summary>
/// <remarks>
/// <para><b>Why a second test project could not do this better.</b> The point is the pair. A client
/// tested against hand-written frames proves it can parse what the test believes the server writes;
/// this proves it can parse what the server writes. The earlier seam defect was exactly that
/// difference — two hand-maintained copies of the same bytes, one of them stale.</para>
///
/// <para><b>What each test is for.</b> The first two say the round trip works at all in both
/// directions a request can end (an answer, and a refusal). The third says a push arrives on
/// <see cref="IViewChannel.Received"/> rather than being mistaken for an answer. The fourth says a
/// server that stops answering turns into a reply and not into a hang — with the timeout shortened
/// by a test seam rather than by waiting thirty seconds.</para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SocketChannelTests : IAsyncLifetime
{
    private const string TkId = "tk-socket";

    private readonly TempToolkitRoot _root = new();
    private ServiceProvider _provider = null!;
    private ViewServer _server = null!;

    public Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddKitXWorkflowV6()
            .AddKitXToolKit(_root.Root)
            .BuildServiceProvider();

        _provider.GetRequiredService<ToolkitStore>().Save(WireToolkit());
        TempToolkitRoot.Write(_root.Root, WireToolkit());
        _provider.GetRequiredService<IToolkitService>().Mount(TkId);

        _server = new ViewServer(_provider.GetRequiredService<Func<IViewChannel>>(), port: 0);
        _server.Start();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _server.Dispose();
        _provider.Dispose();
        _root.Dispose();
        return Task.CompletedTask;
    }

    private static Toolkit WireToolkit() => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "socket" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [new KeyDeclaration("greeting", "string", KeyRole.Out)],
    };

    private async Task<SocketChannel> ConnectAsync()
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_server.Port}/"), CancellationToken.None);

        return new SocketChannel(socket);
    }

    [Fact]
    public async Task A_request_comes_back_as_the_same_kind_of_reply_the_in_process_channel_gives()
    {
        using var channel = await ConnectAsync();

        // `list` is answered with `toolkits`, and the answer travels back through the id table. If
        // the id never matched, this would time out instead of returning.
        var reply = await channel.SendAsync(new ListMessage());

        Assert.True(reply.Succeeded, reply.ErrorMessage);

        var toolkits = Assert.IsType<ToolkitsMessage>(reply.Answer);
        Assert.Equal(TkId, Assert.Single(toolkits.Toolkits).Id);
    }

    [Fact]
    public async Task A_refusal_is_a_reply_with_its_code_rather_than_an_exception()
    {
        using var channel = await ConnectAsync();

        // `delta` is server-to-client only. The server answers with a `rejected` carrying the
        // request's id; the channel turns that back into ChannelReply.Rejected, which is what
        // IViewChannel promises for every business failure.
        var reply = await channel.SendAsync(new DeltaMessage("greeting", JsonSerializer.SerializeToElement("x")));

        Assert.False(reply.Succeeded);
        Assert.Equal(ViewErrorCodes.UnknownCommand, reply.ErrorCode);
        Assert.Null(reply.Answer);
    }

    [Fact]
    public async Task A_push_arrives_on_Received_without_being_mistaken_for_an_answer()
    {
        using var channel = await ConnectAsync();

        var pushes = new List<ViewMessage>();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        channel.Received += (_, message) =>
        {
            lock (pushes)
                pushes.Add(message);

            arrived.TrySetResult();
        };

        var connect = await channel.SendAsync(new ConnectMessage(TkId));
        Assert.True(connect.Succeeded, connect.ErrorMessage);

        // The delta is written by something else — a data-plane write, not a request. This is the
        // only shape in which a frame with no id reaches the client.
        _provider.GetRequiredService<IKeyStore>()
            .Set($"{TkId}/greeting", JsonSerializer.SerializeToElement("pushed"));

        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        lock (pushes)
        {
            var delta = Assert.IsType<DeltaMessage>(pushes.Last());
            Assert.Equal("greeting", delta.Key);
            Assert.Equal("pushed", delta.Value!.Value.GetString());
        }
    }

    [Fact]
    public async Task A_server_that_never_answers_produces_a_timeout_reply_rather_than_a_hang()
    {
        // A real server, whose channel never answers. That is the shape a wedged server actually
        // has — the socket is fine, the request arrived, the answer never comes — and it is the one
        // a client cannot distinguish from a slow one without a deadline.
        using var mute = new ViewServer(() => new MuteChannel(), port: 0);
        mute.Start();

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{mute.Port}/"), CancellationToken.None);

        using var channel = new SocketChannel(socket, answerTimeout: TimeSpan.FromMilliseconds(250));

        var reply = await channel.SendAsync(new ListMessage());

        Assert.False(reply.Succeeded);
        Assert.Equal(SocketChannel.TimeoutCode, reply.ErrorCode);
    }

    [Fact]
    public async Task A_closed_socket_is_a_reply_rather_than_an_exception_escaping_the_caller()
    {
        // The peer vanishes mid-conversation: the request cannot be delivered at all. The interface
        // says a failure to do the thing arrives as ChannelReply.Rejected; only cancellation
        // escapes. A test that accepted a thrown WebSocketException would be blessing a caller that
        // has to catch a transport type to find out its command failed.
        using var mute = new ViewServer(() => new MuteChannel(), port: 0);
        mute.Start();

        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{mute.Port}/"), CancellationToken.None);

        using var channel = new SocketChannel(socket);

        // Close underneath the channel: no close handshake, just the transport going away.
        socket.Abort();

        var reply = await channel.SendAsync(new ListMessage());

        Assert.False(reply.Succeeded);
        Assert.Equal(SocketChannel.DisconnectedCode, reply.ErrorCode);
    }

    /// <summary>A channel that accepts everything and answers nothing.</summary>
    private sealed class MuteChannel : IViewChannel
    {
        public event EventHandler<ViewMessage>? Received
        {
            add { }
            remove { }
        }

        public async ValueTask<ChannelReply> SendAsync(
            ViewMessage message, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new InvalidOperationException("unreachable");
        }
    }
}
