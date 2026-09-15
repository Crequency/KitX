using KitX.Core.Contract.Device;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Shared.CSharp.Plugin;

namespace KitX.ToolKit.Perf.Fakes;

/// <summary>
/// A scripted <see cref="IPluginConnection"/> with a unique <see cref="ConnectionId"/> and
/// a <see cref="PluginInfo"/> whose <c>Name</c> is unique. All send/request operations are
/// no-ops; the routing path only reads <see cref="ConnectionId"/> and <see cref="PluginInfo"/>.
/// </summary>
public sealed class FakeConnection : IPluginConnection
{
    public FakeConnection(string connectionId, string pluginName)
    {
        ConnectionId = connectionId;
        PluginInfo = new PluginInfo { Name = pluginName };
    }

    public string? ConnectionId { get; }

    public PluginInfo? PluginInfo { get; set; }

    public ServerStatus Status => ServerStatus.Running;

    public event EventHandler<PluginMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler? Closed;
    public event EventHandler<PluginResponseEventArgs>? PluginResponse;
    public event EventHandler<PluginStatusReportEventArgs>? StatusReport;

    public void Initialize()
    {
    }

    public void Request(object request)
    {
    }

    public void Send(string message)
    {
    }

    public Task CloseAsync() => Task.CompletedTask;
}
