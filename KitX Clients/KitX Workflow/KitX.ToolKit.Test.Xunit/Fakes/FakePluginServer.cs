using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;

namespace KitX.ToolKit.Test.Xunit.Fakes;

/// <summary>
/// A scripted <see cref="IPluginServer"/> exposing an injected connection list, an
/// id → connection dictionary (<see cref="FindConnection"/> is O(1)), and an externally-raisable
/// <c>PluginMessageReceived</c> event so tests can drive the plugin-event routing path.
/// </summary>
public sealed class FakePluginServer : IPluginServer
{
    private readonly Dictionary<string, IPluginConnection> _byId;

    public FakePluginServer(IReadOnlyList<IPluginConnection> connections)
    {
        Connections = connections;
        _byId = connections.ToDictionary(c => c.ConnectionId ?? string.Empty, StringComparer.Ordinal);
    }

    public int? Port => null;

    public IReadOnlyList<IPluginConnection> Connections { get; }

    public event EventHandler<int>? PortChanged;
    public event EventHandler<PluginDisconnectedEventArgs>? PluginDisconnected;
    public event EventHandler<PluginMessageReceivedEventArgs>? PluginMessageReceived;
    public event EventHandler<PluginRegisteredEventArgs>? PluginRegistered;
    public event EventHandler<PluginUnregisteredEventArgs>? PluginUnregistered;
    public event EventHandler<PluginResponseEventArgs>? PluginResponse;

    public IPluginServer Run() => this;

    public void Stop()
    {
    }

    public IPluginConnector? FindConnector(PluginInfo pluginInfo) => null;

    public IPluginConnection? FindConnection(string connectionId)
        => _byId.TryGetValue(connectionId, out var c) ? c : null;

    /// <summary>Raises <c>PluginMessageReceived</c> for the given connection id and raw message.</summary>
    public void RaiseMessage(string connectionId, string message)
        => RaiseMessage(connectionId, message, null, null);

    /// <summary>
    /// Raises <c>PluginMessageReceived</c> with an optional already-parsed command, so tests can
    /// exercise the parse-once path where the router consumes <see cref="PluginMessageReceivedEventArgs.Command"/>
    /// instead of deserializing <paramref name="message"/> itself.
    /// </summary>
    public void RaiseMessage(string connectionId, string message, Command? command = null, bool? isResponse = null)
        => PluginMessageReceived?.Invoke(this, new PluginMessageReceivedEventArgs
        {
            ConnectionId = connectionId,
            Message = message,
            Command = command,
            IsResponse = isResponse,
        });
}
