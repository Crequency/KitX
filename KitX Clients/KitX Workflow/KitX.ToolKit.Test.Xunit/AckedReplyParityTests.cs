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
/// The same command, sent twice: once through the in-process channel and once over a socket. What
/// comes back has to be the same answer, values included.
/// </summary>
/// <remarks>
/// <para><b>Why two channels in one test instead of one channel and an expectation.</b> The defect
/// this exists for was not "the socket returns nothing" — it was "the socket returns less than the
/// in-process path does, and nobody compared the two". An assertion written as "the socket reply
/// carries a <c>started</c> value" would have been green under the old code only if the old code
/// had been asked; nobody was asking, because nothing consumed the field. Comparing the two sides
/// makes the in-process behaviour the specification, which is where it actually lives:
/// <c>ViewChannel</c> built an <c>AckedReply</c> and put it in <c>Accepted</c>.</para>
///
/// <para><b>Why <c>event</c> rather than <c>trigger</c> or the three removal commands.</b>
/// <c>event</c> replies with <c>started</c> and needs one trigger declaration; <c>trigger</c> needs
/// a spawnable manual trigger and the same shape; the removal commands need three separate key
/// fixtures to reach all three of their codes. One representative is enough to pin the rule, and
/// the rule is "an ack's values survive", not "this particular command works".</para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class AckedReplyParityTests : IAsyncLifetime
{
    private const string TkId = "tk-ack";

    private readonly TempToolkitRoot _root = new();
    private ServiceProvider _provider = null!;
    private ViewServer _server = null!;

    /// <summary>A ToolKit with one UI-event trigger, so an <c>event</c> command has something to
    /// match: <c>started</c> is populated only when a trigger of that name exists.</summary>
    private static Toolkit AckToolkit() => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "ack" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [new KeyDeclaration("status", "string", KeyRole.Out)],
        Triggers =
        [
            new Trigger
            {
                Id = "scan-click",
                Type = TriggerType.UIEvent,
                Config = new TriggerConfig { Event = "scan" },
                Bindings = [new TriggerBinding { Workflow = "wf" }],
            },
        ],
    };

    public Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddKitXWorkflowV6()
            .AddKitXToolKit(_root.Root)
            .BuildServiceProvider();

        _provider.GetRequiredService<ToolkitStore>().Save(AckToolkit());
        TempToolkitRoot.Write(_root.Root, AckToolkit());
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

    /// <summary>The one command both halves send.</summary>
    private static CommandMessage Press() => new(ViewCommands.Event) { EventName = "scan" };

    [Fact]
    public async Task An_ack_carries_the_same_values_over_a_socket_as_it_does_in_process()
    {
        // In process first: this side is the specification, and it is the one that was always right.
        var inProcess = _provider.GetRequiredService<Func<IViewChannel>>()();

        try
        {
            var connect = await inProcess.SendAsync(new ConnectMessage(TkId));
            Assert.True(connect.Succeeded, connect.ErrorMessage);

            var local = await inProcess.SendAsync(Press());
            Assert.True(local.Succeeded, local.ErrorMessage);

            var localAck = Assert.IsType<AckedReply>(local.Answer);

            // Values is nullable in the contract because a bare acknowledgement has none. Naming the
            // non-null one here is not defensive noise: it is the fixture's claim that this command
            // answers with data, and the comparison below is meaningless if it does not.
            var localValues = Assert.IsAssignableFrom<IReadOnlyDictionary<string, JsonElement>>(
                localAck.Values);

            // Not "non-empty": a fixture with no trigger would answer with an empty `started`, and
            // then the comparison below would pass for a trivial reason.
            Assert.NotEmpty(localValues);

            // Now the same command over a socket.
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_server.Port}/"), CancellationToken.None);
            using var channel = new SocketChannel(socket);

            var remoteConnect = await channel.SendAsync(new ConnectMessage(TkId));
            Assert.True(remoteConnect.Succeeded, remoteConnect.ErrorMessage);

            var remote = await channel.SendAsync(Press());
            Assert.True(remote.Succeeded, remote.ErrorMessage);

            var remoteAck = Assert.IsType<AckedReply>(remote.Answer);
            var remoteValues = Assert.IsAssignableFrom<IReadOnlyDictionary<string, JsonElement>>(
                remoteAck.Values);

            // Keys, then shape. The ids themselves cannot be compared: each press spawns a new flow,
            // so both sides are supposed to differ there. Comparing raw text looked stricter and was
            // simply wrong — it failed against correct code, which is what a measurement taken
            // before the question is understood costs.
            Assert.Equal(
                localValues.Select(pair => pair.Key).OrderBy(k => k, StringComparer.Ordinal),
                remoteValues.Select(pair => pair.Key).OrderBy(k => k, StringComparer.Ordinal));

            // Same count of flows on both sides: this is the part that the old code could not have
            // produced, because it answered with no values at all.
            Assert.Equal(
                localValues["started"].GetArrayLength(),
                remoteValues["started"].GetArrayLength());

            Assert.NotEqual(0, remoteValues["started"].GetArrayLength());
        }
        finally
        {
            (inProcess as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task The_command_name_survives_the_socket_too()
    {
        // `Command` is the other half of an ack, and it is what lets a client match an answer to the
        // thing it asked — the id matches the request, this matches the shape. It is set in process
        // and travelled in the same frame, so it is the same comparison from a different angle.
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_server.Port}/"), CancellationToken.None);
        using var channel = new SocketChannel(socket);

        Assert.True((await channel.SendAsync(new ConnectMessage(TkId))).Succeeded);

        var remote = await channel.SendAsync(Press());

        var ack = Assert.IsType<AckedReply>(remote.Answer);

        Assert.Equal(ViewCommands.Event, ack.Command);
    }
}
