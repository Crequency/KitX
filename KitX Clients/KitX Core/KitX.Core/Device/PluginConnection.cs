using System;
using System.Text.Json;
using Fleck;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Contract.Device;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Plugin connection implementation
/// </summary>
public class PluginConnection : KitX.Core.Contract.Plugin.IPluginConnection
{
    private readonly IWebSocketConnection _connection;
    private KitX.Core.Contract.Device.ServerStatus _statusBackingField = KitX.Core.Contract.Device.ServerStatus.Pending;

    /// <summary>
    /// Gets the connection ID
    /// </summary>
    public string? ConnectionId { get; private set; }

    /// <summary>
    /// Gets or sets the plugin info
    /// </summary>
    public KitX.Shared.CSharp.Plugin.PluginInfo? PluginInfo { get; set; }

    /// <summary>
    /// Gets the connection status
    /// </summary>
    public KitX.Core.Contract.Device.ServerStatus Status => _statusBackingField;

    /// <summary>
    /// Event raised when a message is received
    /// </summary>
    public event EventHandler<string>? MessageReceived;

    /// <summary>
    /// Event raised when connection is closed
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Event raised when a plugin response is received (IPluginConnector implementation)
    /// </summary>
    public event EventHandler<KitX.Core.Contract.Plugin.Events.PluginResponseEventArgs>? PluginResponse;

    /// <summary>
    /// Event raised when plugin reports status (IPluginConnector implementation)
    /// </summary>
    public event EventHandler<KitX.Core.Contract.Plugin.Events.PluginStatusReportEventArgs>? StatusReport;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="connection">The WebSocket connection</param>
    /// <param name="connectionId">The connection ID</param>
    public PluginConnection(IWebSocketConnection connection, string connectionId)
    {
        _connection = connection;
        ConnectionId = connectionId;
    }

    /// <summary>
    /// Initializes the connection
    /// </summary>
    public void Initialize()
    {
        _connection.OnOpen = () =>
        {
            _statusBackingField = KitX.Core.Contract.Device.ServerStatus.Running;
            StatusReport?.Invoke(this, new PluginStatusReportEventArgs
            {
                ConnectionId = ConnectionId!,
                Status = KitX.Core.Contract.Device.ServerStatus.Running.ToString()
            });
        };

        _connection.OnMessage = message =>
        {
            // Handle plugin response messages
            try
            {
                var kwc = JsonSerializer.Deserialize<Request>(message, PluginsServer.SerializerOptions);
                if (kwc?.Content is not null)
                {
                    var command = JsonSerializer.Deserialize<Command>(kwc.Content, PluginsServer.SerializerOptions);
                    if (command.Tags != null &&
                        command.Tags.TryGetValue("RequestId", out var requestId))
                    {
                        // This is a plugin response - trigger PluginResponse event
                        PluginResponse?.Invoke(this, new KitX.Core.Contract.Plugin.Events.PluginResponseEventArgs
                        {
                            RequestId = requestId,
                            Content = kwc.Content
                        });
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error parsing plugin response message");
            }

            // Forward to MessageReceived for other handlers
            MessageReceived?.Invoke(this, message);
        };

        _connection.OnClose = () =>
        {
            _statusBackingField = KitX.Core.Contract.Device.ServerStatus.Pending;
            StatusReport?.Invoke(this, new KitX.Core.Contract.Plugin.Events.PluginStatusReportEventArgs
            {
                ConnectionId = ConnectionId!,
                Status = KitX.Core.Contract.Device.ServerStatus.Pending.ToString()
            });
            Closed?.Invoke(this, EventArgs.Empty);
        };

        _connection.OnError = ex =>
        {
            _statusBackingField = KitX.Core.Contract.Device.ServerStatus.Errored;
            StatusReport?.Invoke(this, new KitX.Core.Contract.Plugin.Events.PluginStatusReportEventArgs
            {
                ConnectionId = ConnectionId!,
                Status = KitX.Core.Contract.Device.ServerStatus.Errored.ToString()
            });
            Serilog.Log.Error(ex, $"PluginConnection error for {ConnectionId}, triggering Closed event");

            // Also trigger Closed event when error occurs (e.g., remote host disconnected abruptly)
            Closed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Sends a message
    /// </summary>
    /// <param name="message">The message to send</param>
    public void Send(string message)
    {
        _connection.Send(message);
    }

    /// <summary>
    /// Sends a request to the plugin (IPluginConnector implementation)
    /// </summary>
    /// <param name="request">The request to send</param>
    public void Request(object request)
    {
        var json = JsonSerializer.Serialize(request, PluginsServer.SerializerOptions);
        _connection.Send(json);
    }

    /// <summary>
    /// Closes the connection
    /// </summary>
    public async System.Threading.Tasks.Task CloseAsync()
    {
        await System.Threading.Tasks.Task.Run(() =>
        {
            _connection.Close();
        });
    }
}