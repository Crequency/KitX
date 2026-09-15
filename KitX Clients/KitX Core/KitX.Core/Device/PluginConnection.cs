using System.Text.Json;
using Fleck;
using KitX.Core.Contract.Plugin.Events;
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
    /// Event raised when a message is received. Carries the raw message plus the
    /// already-deserialized <see cref="Request"/> / <see cref="Command"/> so downstream
    /// handlers do not re-deserialize the same message.
    /// </summary>
    public event EventHandler<PluginMessageReceivedEventArgs>? MessageReceived;

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
            // Parse Request + Command once here and share the result with downstream handlers
            // (PluginsServer, and through it the ToolKit event router) so each message is not
            // re-deserialized on every hop of the chain.
            Request? request = null;
            Command? command = null;
            bool isResponse = false;

            // Handle plugin response messages
            try
            {
                request = JsonSerializer.Deserialize<Request>(message, PluginsServer.SerializerOptions);
                if (request?.Content is not null)
                {
                    command = JsonSerializer.Deserialize<Command>(request.Content, PluginsServer.SerializerOptions);
                    if (command is not null &&
                        command.Value.Tags != null &&
                        command.Value.Tags.TryGetValue("RequestId", out var requestId))
                    {
                        // This is a plugin response - trigger PluginResponse event.
                        // Responses are not forwarded as plugin messages.
                        isResponse = true;
                        PluginResponse?.Invoke(this, new KitX.Core.Contract.Plugin.Events.PluginResponseEventArgs
                        {
                            RequestId = requestId,
                            Content = request.Content
                        });
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error parsing plugin response message");
            }

            // Forward to MessageReceived for other handlers, carrying the parsed results
            // (null when parsing failed so consumers fall back to self-parsing).
            MessageReceived?.Invoke(this, new PluginMessageReceivedEventArgs
            {
                ConnectionId = ConnectionId!,
                Message = message,
                Request = request,
                Command = command,
                IsResponse = isResponse
            });
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