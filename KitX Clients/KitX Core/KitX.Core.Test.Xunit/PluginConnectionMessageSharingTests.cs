using System.Text;
using System.Text.Json;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Device;
using KitX.Core.Test.Xunit.Fakes;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;

namespace KitX.Core.Test.Xunit;

/// <summary>
/// Verifies that <see cref="PluginConnection"/> parses each incoming message once and shares the
/// parsed <see cref="Request"/> / <see cref="Command"/> with downstream handlers through
/// <see cref="PluginConnection.MessageReceived"/> (G1 parse-once), while responses continue to
/// flow exclusively through the <see cref="PluginConnection.PluginResponse"/> channel.
/// </summary>
public class PluginConnectionMessageSharingTests
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

    private static (FakeWebSocketConnection Sock, PluginConnection Connection) CreateConnection()
    {
        var sock = new FakeWebSocketConnection();
        var connection = new PluginConnection(sock, Guid.NewGuid().ToString());
        connection.Initialize();
        return (sock, connection);
    }

    [Fact]
    public void NormalCommand_ForwardsMessageReceived_WithParsedRequestAndCommand()
    {
        var (sock, connection) = CreateConnection();
        PluginMessageReceivedEventArgs? received = null;
        connection.MessageReceived += (_, e) => received = e;

        sock.Deliver(CommandMessage("SayHello", new() { ["msg"] = "hi" }));

        Assert.NotNull(received);
        Assert.Equal(connection.ConnectionId, received!.ConnectionId);
        Assert.NotNull(received.Request);
        Assert.NotNull(received.Command);
        Assert.False(received.IsResponse);
        Assert.Equal("SayHello", received.Command!.Value.Request);
        Assert.Equal("hi", received.Command.Value.Tags["msg"]);
    }

    [Fact]
    public void RegisterPlugin_ForwardsMessageReceived_WithParsedRegisterCommand()
    {
        var (sock, connection) = CreateConnection();
        PluginMessageReceivedEventArgs? received = null;
        connection.MessageReceived += (_, e) => received = e;

        sock.Deliver(RegisterPluginMessage("MyPlugin"));

        Assert.NotNull(received);
        Assert.NotNull(received!.Command);
        Assert.Equal(CommandRequestInfo.RegisterPlugin, received.Command.Value.Request);
        // The plugin-info body is preserved so PluginsServer can register without re-parsing.
        Assert.True(received.Command.Value.BodyLength > 0);
    }

    [Fact]
    public void TriggerFired_ForwardsMessageReceived_WithParsedCommand()
    {
        var (sock, connection) = CreateConnection();
        PluginMessageReceivedEventArgs? received = null;
        connection.MessageReceived += (_, e) => received = e;

        sock.Deliver(CommandMessage(CommandRequestInfo.TriggerFired, new() { ["TriggerName"] = "t1" }));

        Assert.NotNull(received);
        Assert.NotNull(received!.Command);
        Assert.Equal(CommandRequestInfo.TriggerFired, received.Command.Value.Request);
        Assert.Equal("t1", received.Command.Value.Tags["TriggerName"]);
    }

    [Fact]
    public void ResponseWithRequestId_RaisesPluginResponse_AndNotMessageReceived()
    {
        var (sock, connection) = CreateConnection();
        PluginResponseEventArgs? response = null;
        var messageReceivedRaised = false;
        connection.PluginResponse += (_, e) => response = e;
        connection.MessageReceived += (_, _) => messageReceivedRaised = true;

        sock.Deliver(CommandMessage("AnyCommand", new() { ["RequestId"] = "r-123" }));

        Assert.NotNull(response);
        Assert.Equal("r-123", response!.RequestId);
        Assert.False(messageReceivedRaised, "A response must not be forwarded as a plugin message.");
    }
}
