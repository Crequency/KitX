using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Device;
using KitX.Core.Test.Xunit.Fakes;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// End-to-end tests over a real WebSocket verifying the G1 parse-once chain: a message is parsed
/// by <see cref="PluginConnection"/> and the parsed <see cref="Request"/> / <see cref="Command"/>
/// are shared down to the <see cref="IPluginServer.PluginMessageReceived"/> event, while behavior
/// (registration, response routing) stays equivalent to the pre-refactor chain.
/// </summary>
public class PluginsServerParseOnceTests
{
    private static string CommandMessage(string request, Dictionary<string, string>? tags = null,
        byte[]? body = null, int bodyLength = 0)
    {
        var command = new Command
        {
            Request = request,
            Tags = tags ?? new Dictionary<string, string>(),
            Body = body ?? Array.Empty<byte>(),
            BodyLength = bodyLength,
        };
        return JsonSerializer.Serialize(new Request { Content = JsonSerializer.Serialize(command) });
    }

    private static string RegisterPluginMessage(string pluginName)
    {
        var pluginInfo = JsonSerializer.Serialize(new PluginInfo { Name = pluginName });
        var body = Encoding.UTF8.GetBytes(pluginInfo);
        return CommandMessage(CommandRequestInfo.RegisterPlugin, null, body, body.Length);
    }

    private static async Task<ClientWebSocket> ConnectAsync(int port, string connectionId, CancellationToken ct)
    {
        var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/{connectionId}"), ct);
        return client;
    }

    private static Task SendAsync(ClientWebSocket client, string message, CancellationToken ct)
        => client.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);

    [Fact]
    public async Task RegisterPlugin_RegistersPlugin_AndPluginMessageReceivedCarriesParsedResults()
    {
        var eventService = new FakeEventService();
        var server = new PluginsServer(eventService);
        var originalPluginsServerPort = ConstantTable.PluginsServerPort;
        var received = new ConcurrentQueue<PluginMessageReceivedEventArgs>();
        server.PluginMessageReceived += (_, e) => received.Enqueue(e);

        try
        {
            server.ConfigurePort(GetFreePort());
            server.Run();
            Assert.NotNull(server.Port);

            var connectionId = Guid.NewGuid().ToString();
            using var client = await ConnectAsync(server.Port!.Value, connectionId, CancellationToken.None);

            var pluginName = "ParseOncePlugin";
            var message = RegisterPluginMessage(pluginName);
            await SendAsync(client, message, CancellationToken.None);

            var registered = await WaitUntilAsync(
                () => eventService.WasPublished(EventNames.PluginRegistered),
                TimeSpan.FromSeconds(5));
            Assert.True(registered, "RegisterPlugin message did not produce a PluginRegistered event.");

            var connection = server.FindConnection(connectionId);
            Assert.NotNull(connection);
            Assert.Equal(pluginName, connection!.PluginInfo?.Name);

            // PluginMessageReceived must carry the already-parsed result consistent with the raw message.
            var args = received.SingleOrDefault(e => e.Message == message);
            Assert.NotNull(args);
            Assert.Equal(connectionId, args!.ConnectionId);
            Assert.NotNull(args.Request);
            Assert.NotNull(args.Command);
            Assert.False(args.IsResponse);
            Assert.Equal(CommandRequestInfo.RegisterPlugin, args.Command!.Value.Request);
        }
        finally
        {
            ConstantTable.PluginsServerPort = originalPluginsServerPort;
            await server.Close();
        }
    }

    [Fact]
    public async Task NormalCommand_PluginMessageReceivedCarriesParsedResults()
    {
        var eventService = new FakeEventService();
        var server = new PluginsServer(eventService);
        var originalPluginsServerPort = ConstantTable.PluginsServerPort;
        var received = new ConcurrentQueue<PluginMessageReceivedEventArgs>();
        server.PluginMessageReceived += (_, e) => received.Enqueue(e);

        try
        {
            server.ConfigurePort(GetFreePort());
            server.Run();
            Assert.NotNull(server.Port);

            var connectionId = Guid.NewGuid().ToString();
            using var client = await ConnectAsync(server.Port!.Value, connectionId, CancellationToken.None);

            var message = CommandMessage("SayHello", new() { ["msg"] = "hi" });
            await SendAsync(client, message, CancellationToken.None);

            var got = await WaitUntilAsync(
                () => received.Any(e => e.Message == message),
                TimeSpan.FromSeconds(5));
            Assert.True(got, "Normal command did not reach PluginMessageReceived.");

            var args = received.Single(e => e.Message == message);
            Assert.NotNull(args.Command);
            Assert.False(args.IsResponse);
            Assert.Equal("SayHello", args.Command!.Value.Request);
            Assert.Equal("hi", args.Command.Value.Tags["msg"]);
        }
        finally
        {
            ConstantTable.PluginsServerPort = originalPluginsServerPort;
            await server.Close();
        }
    }

    [Fact]
    public async Task ResponseMessage_RoutesToPluginResponse_NotToPluginMessageReceived()
    {
        var eventService = new FakeEventService();
        var server = new PluginsServer(eventService);
        var originalPluginsServerPort = ConstantTable.PluginsServerPort;
        var received = new ConcurrentQueue<PluginMessageReceivedEventArgs>();
        server.PluginMessageReceived += (_, e) => received.Enqueue(e);

        try
        {
            server.ConfigurePort(GetFreePort());
            server.Run();
            Assert.NotNull(server.Port);

            var connectionId = Guid.NewGuid().ToString();
            using var client = await ConnectAsync(server.Port!.Value, connectionId, CancellationToken.None);

            var message = CommandMessage("AnyCommand", new() { ["RequestId"] = "r-123" });
            await SendAsync(client, message, CancellationToken.None);

            var responded = await WaitUntilAsync(
                () => eventService.WasPublished(EventNames.PluginResponse),
                TimeSpan.FromSeconds(5));
            Assert.True(responded, "Response message did not produce a PluginResponse event.");

            // Give any (incorrect) forwarding a chance to arrive, then assert none did.
            await Task.Delay(300);
            Assert.DoesNotContain(received, e => e.Message == message);
        }
        finally
        {
            ConstantTable.PluginsServerPort = originalPluginsServerPort;
            await server.Close();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }
        return condition();
    }
}
