using System.Net.WebSockets;
using System.Text;
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
/// The View protocol over a real socket: a WebSocket client written by hand, talking to
/// <see cref="ViewServer"/> with nothing but JSON text.
/// </summary>
/// <remarks>
/// <para><b>Why this is not another unit test of <c>ViewChannel</c>.</b> Every layer of this chain
/// was already tested — the contract, the projection, the fold, the renderer. What had never been
/// exercised is the seam between them and a socket, and that seam is where a wire protocol is
/// actually decided: the frame envelope, the correlation id, what a push looks like, and what comes
/// back for a message the server refuses. A test that calls <c>SendAsync</c> directly proves none of
/// those, because it has no envelope at all.</para>
///
/// <para><b>The client is BCL, deliberately</b> — <see cref="ClientWebSocket"/> and
/// <see cref="JsonSerializer"/>, no package. A hand-written client that shares nothing with the
/// server except the contract is the only kind that can disagree with it; a client built on the
/// same helper methods would agree by construction.</para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ViewServerWireTests : IAsyncLifetime
{
    private const string TkId = "tk-wire";

    private readonly TempToolkitRoot _root = new();
    private ServiceProvider _provider = null!;
    private ViewServer _server = null!;

    private static Toolkit WireToolkit() => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "wire" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],

        // Declared so both halves of the protocol agree about it: `welcome` carries declared keys
        // with values, and a write that is not declared would be filtered out by the session
        // before it reached the socket — a push test that stayed green by silence.
        Keys = [new KeyDeclaration("greeting", "string", KeyRole.Out)],
    };

    public Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddKitXWorkflowV6()
            .AddKitXToolKit(_root.Root)
            .BuildServiceProvider();

        // Written through the store rather than the management facade: the facade has no Save,
        // and this test is not about how a ToolKit gets onto disk — it is about what happens once
        // one is mounted and a socket is pointed at it.
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

    /// <summary>Connects, and returns a client plus a reader for the frames it sends.</summary>
    private async Task<(ClientWebSocket Socket, WireClient Client)> ConnectAsync()
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_server.Port}/"), CancellationToken.None);
        return (socket, new WireClient(socket));
    }

    private static string Frame(string id, object message) =>
        JsonSerializer.Serialize(new { id, message }, ViewWire.Options);

    private static string Frame(object message) =>
        JsonSerializer.Serialize(new { message }, ViewWire.Options);

    [Fact]
    public async Task A_list_comes_back_under_the_id_it_was_asked_with()
    {
        var (socket, client) = await ConnectAsync();
        using var _ = socket;

        await client.SendAsync(Frame("1", new { type = "list" }));

        var reply = await client.ReceiveAsync();

        Assert.Equal("1", reply.Id);

        var toolkits = reply.Message as ToolkitsMessage;
        Assert.NotNull(toolkits);

        // The id is the point of this test; the row is what proves the answer is about our ToolKit
        // rather than about an empty server.
        var only = Assert.Single(toolkits!.Toolkits);
        Assert.Equal(TkId, only.Id);
    }

    [Fact]
    public async Task A_welcome_carries_both_the_values_and_the_card()
    {
        // Two facts in one frame, and the second is the one that is easy to lose: `surface` is a
        // field of `welcome`, not a separate message. A client that waited for a `surface` push
        // after connecting would draw an empty card and never be corrected, because an unchanged
        // layout raises no event.
        var (socket, client) = await ConnectAsync();
        using var _ = socket;

        _provider.GetRequiredService<IKeyStore>().Set(
            $"{TkId}/greeting", JsonSerializer.SerializeToElement("hello"));

        await client.SendAsync(Frame(
            "7",
            new { type = "connect", toolkitId = TkId, protocolVersion = ConnectMessage.CurrentProtocolVersion }));

        var reply = await client.ReceiveAsync();

        Assert.Equal("7", reply.Id);

        var welcome = Assert.IsType<WelcomeMessage>(reply.Message);
        Assert.Equal(TkId, welcome.ToolkitId);
        Assert.True(welcome.Values.ContainsKey("greeting"), "the seeded value should be in the welcome");
        Assert.Equal("hello", welcome.Values["greeting"].GetString());

        // Not asserting that the document has elements — the sample writes no surface here — but
        // that the field arrived at all, which is what a renderer needs before it can decide the
        // card is empty.
        Assert.NotNull(welcome.Surface);
    }

    [Fact]
    public async Task A_key_written_by_someone_else_arrives_as_a_push_with_no_id()
    {
        // The push is not an answer, so it carries no id. That is the envelope rule this whole
        // design rests on, and the only way to observe it is to watch a frame the client did not
        // ask for.
        var (socket, client) = await ConnectAsync();
        using var _ = socket;

        await client.SendAsync(Frame(
            "1",
            new { type = "connect", toolkitId = TkId, protocolVersion = ConnectMessage.CurrentProtocolVersion }));

        await client.ReceiveAsync();

        _provider.GetRequiredService<IKeyStore>().Set(
            $"{TkId}/greeting", JsonSerializer.SerializeToElement("typed by someone"));

        var push = await client.ReceiveAsync();

        Assert.Null(push.Id);

        var delta = Assert.IsType<DeltaMessage>(push.Message);
        Assert.Equal("greeting", delta.Key);
        Assert.Equal("typed by someone", delta.Value!.Value.GetString());
    }

    [Fact]
    public async Task A_push_only_message_sent_by_the_client_is_refused_under_its_own_id()
    {
        // The refusal is not the interesting part — ViewChannel already refuses it. What this
        // pins is that the refusal travels back on the envelope id, because a client that cannot
        // match a refusal to its request has to guess which of its in-flight messages failed.
        var (socket, client) = await ConnectAsync();
        using var _ = socket;

        await client.SendAsync(Frame("9", new { type = "delta", key = "greeting", value = "x" }));

        var reply = await client.ReceiveAsync();

        Assert.Equal("9", reply.Id);

        var rejected = Assert.IsType<RejectedReply>(reply.Message);
        Assert.Equal(ViewErrorCodes.UnknownCommand, rejected.Code);
    }

    /// <summary>A frame as it arrived, after the envelope was read.</summary>
    private sealed record ReceivedFrame(string? Id, ViewMessage? Message, string Raw);

    /// <summary>
    /// The hand-written client: one socket, one buffer, and enough JSON to say what it means.
    /// </summary>
    private sealed class WireClient(ClientWebSocket socket)
    {
        public Task SendAsync(string text) =>
            socket.SendAsync(
                Encoding.UTF8.GetBytes(text),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);

        /// <summary>Reads one whole frame, failing the test rather than the run when none arrives.</summary>
        public async Task<ReceivedFrame> ReceiveAsync(int timeoutMs = 5000)
        {
            using var timeout = new CancellationTokenSource(timeoutMs);

            var buffer = new byte[16 * 1024];
            var accumulated = new MemoryStream();

            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer, timeout.Token);

                    Assert.Equal(WebSocketMessageType.Text, result.MessageType);

                    accumulated.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                Assert.Fail(
                    $"no frame arrived within {timeoutMs} ms; " +
                    "a silent socket is the failure mode this test exists to make loud");
            }

            var text = Encoding.UTF8.GetString(accumulated.ToArray());

            using var document = JsonDocument.Parse(text);

            var id = document.RootElement.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;

            var message = document.RootElement.GetProperty("message").Deserialize<ViewMessage>(ViewWire.Options);

            return new ReceivedFrame(id, message, text);
        }
    }
}
