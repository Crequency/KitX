using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fleck;
using KitX.Core;
using KitX.Core.Contract.Plugin;
using KitX.Core.Event;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using Serilog;
using CTask = System.Threading.Tasks.Task;

namespace KitX.Core.Device;

/// <summary>
/// Plugins server for WebSocket connections
/// </summary>
public class PluginsServer : IPluginServer
{
    private static PluginsServer? _instance;

    /// <summary>
    /// Gets the singleton instance
    /// </summary>
    internal static PluginsServer Instance => _instance ??= new();

    private WebSocketServer? _server;
    private readonly List<IPluginConnection> _connections = new();
    private ServerStatus _status = ServerStatus.Pending;

    /// <summary>
    /// JSON serializer options (accessible from PluginConnection)
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Gets the service status
    /// </summary>
    public ServerStatus Status => _status;

    /// <summary>
    /// Gets or sets the port
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    /// Event raised when server port changes
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<int>? PortChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Gets the list of plugin connections
    /// </summary>
    public IReadOnlyList<IPluginConnection> Connections => _connections.AsReadOnly();

    /// <summary>
    /// IPluginServer.Connections — returns connected plugins as IPluginConnector list
    /// </summary>
    IReadOnlyList<IPluginConnector> IPluginServer.Connections =>
        _connections.Cast<IPluginConnector>().ToList().AsReadOnly();

    /// <summary>
    /// Event raised when a plugin connects
    /// </summary>
    public event EventHandler<PluginConnectedEventArgs>? PluginConnected;

    /// <summary>
    /// Event raised when a plugin disconnects
    /// </summary>
    public event EventHandler<PluginDisconnectedEventArgs>? PluginDisconnected;

    /// <summary>
    /// Event raised when a plugin message is received
    /// </summary>
    public event EventHandler<PluginMessageReceivedEventArgs>? PluginMessageReceived;

    /// <summary>
    /// Event raised when a plugin registers with the server (interface implementation)
    /// </summary>
    public event EventHandler<PluginRegisteredEventArgs>? PluginRegistered;

    /// <summary>
    /// Event raised when a plugin unregisters/disconnects from the server (interface implementation)
    /// </summary>
    public event EventHandler<PluginUnregisteredEventArgs>? PluginUnregistered;

    /// <summary>
    /// Event raised when a plugin sends a response (has RequestId)
    /// </summary>
    public event EventHandler<PluginResponseEventArgs>? PluginResponse;

    /// <summary>
    /// Private constructor
    /// </summary>
    private PluginsServer()
    {
    }

    /// <summary>
    /// Runs the plugins server with retry logic for port conflicts
    /// </summary>
    /// <returns>The server instance</returns>
    public IPluginServer Run()
    {
        if (_status != ServerStatus.Pending)
            return this;

        _status = ServerStatus.Starting;

        // Initialize RealPluginManager when server starts, so it can receive plugin messages
        try
        {
            _ = new KitX.Core.Workflow.RealPluginManager(this);
            Log.Information("[PluginsServer] RealPluginManager initialized for message handling");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginsServer] Failed to initialize RealPluginManager");
        }

        const int maxRetries = 10;
        const int startPort = 7777;
        int currentPort = startPort;
        bool serverStarted = false;

        for (int retryCount = 0; retryCount < maxRetries && !serverStarted; retryCount++)
        {
            try
            {
                // Determine port for this attempt
                if (ConstantTable.PluginsServerPort <= 0)
                {
                    currentPort = startPort + retryCount;
                    // Accept connections on all network interfaces (legacy protocol compatible)
                    _server = new WebSocketServer($"ws://0.0.0.0:{currentPort}");
                }
                else
                {
                    // Use configured port
                    currentPort = ConstantTable.PluginsServerPort;
                    _server = new WebSocketServer($"ws://0.0.0.0:{currentPort}");
                }

                _server!.Start(socket =>
                {
                    var connectionId = socket.ConnectionInfo.Path.Trim('/');

                    if (RegexToVerifyConnectionId().IsMatch(connectionId) == false)
                    {
                        socket.Send("Invalid connection id.");
                        socket.Close();
                        return;
                    }

                    var connection = new PluginConnection(socket, connectionId);
                    _connections.Add(connection);

                    // Handle connection closed
                    connection.Closed += (sender, args) =>
                    {
                        _connections.Remove(connection);

                        Log.Information($"[PluginsServer] Connection closed: {connectionId}, PluginInfo: {connection.PluginInfo?.Name}");

                        // Trigger PluginUnregistered if this connection had a registered plugin
                        if (connection.PluginInfo is not null)
                        {
                            Log.Information($"[PluginsServer] Publishing PluginUnregistered for: {connection.PluginInfo.Name}");

                            PluginUnregistered?.Invoke(this, new PluginUnregisteredEventArgs
                            {
                                PluginInfo = connection.PluginInfo
                            });

                            // Publish event via EventService
                            EventService.Instance.Publish(EventNames.PluginUnregistered, new PluginEventArgs
                            {
                                PluginInfo = connection.PluginInfo
                            });
                        }

                        Log.Information($"[PluginsServer] Publishing PluginDisconnected for: {connectionId}");

                        PluginDisconnected?.Invoke(this, new PluginDisconnectedEventArgs
                        {
                            ConnectionId = connectionId
                        });

                        // Publish event via EventService
                        EventService.Instance.Publish(EventNames.PluginDisconnected, new PluginConnectionEventArgs
                        {
                            ConnectionId = connectionId,
                            PluginInfo = connection.PluginInfo
                        });
                    };

                    connection.MessageReceived += (sender, message) =>
                    {
                        try
                        {
                            var kwc = System.Text.Json.JsonSerializer.Deserialize<Request>(message);
                            if (kwc?.Content is not null)
                            {
                                var cmd = System.Text.Json.JsonSerializer.Deserialize<Command>(kwc.Content);
                                if (cmd.Request == KitX.Shared.CSharp.WebCommand.Infos.CommandRequestInfo.RegisterPlugin)
                                {
                                    var body = System.Text.Encoding.UTF8.GetString(cmd.Body.AsSpan(0, cmd.BodyLength).ToArray());
                                    var pluginInfo = System.Text.Json.JsonSerializer.Deserialize<PluginInfo>(body);
                                    if (pluginInfo is not null)
                                    {
                                        pluginInfo.Tags ??= new();
                                        pluginInfo.Tags[nameof(PluginConnection.ConnectionId)] = connectionId;
                                        pluginInfo.Tags["JoinTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss(FF)");
                                        connection.PluginInfo = pluginInfo;
                                        PluginRegistered?.Invoke(this, new PluginRegisteredEventArgs
                                        {
                                            PluginInfo = pluginInfo
                                        });

                                        // Publish event via EventService
                                        EventService.Instance.Publish(EventNames.PluginRegistered, new PluginEventArgs
                                        {
                                            PluginInfo = pluginInfo
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "[PluginsServer] Error handling plugin message");
                        }

                        Log.Information($"[PluginsServer] Invoking PluginMessageReceived event for connection {connectionId}");
                        PluginMessageReceived?.Invoke(this, new PluginMessageReceivedEventArgs
                        {
                            ConnectionId = connectionId,
                            Message = message
                        });
                    };

                    // Forward PluginResponse events from PluginConnection to PluginsServer.PluginResponse
                    connection.PluginResponse += (sender, args) =>
                    {
                        Log.Information($"[PluginsServer] Forwarding PluginResponse event, RequestId: {args.RequestId}");
                        PluginResponse?.Invoke(this, args);
                    };

                    connection.Initialize();

                    PluginConnected?.Invoke(this, new PluginConnectedEventArgs
                    {
                        ConnectionId = connectionId
                    });
                });

                Port = _server!.Port;
                serverStarted = true;

                // Update ConstantTable with the actual port
                ConstantTable.PluginsServerPort = Port ?? 0;

                Log.Information($"[PluginsServer] PluginsServer started on port {Port}");

                // Publish port changed event via EventService only (removed direct PortChanged event to avoid potential recursion)
                EventService.Instance.Publish(EventNames.PluginsServerPortChanged, new PortChangedEventArgs { Port = Port ?? 0 });
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Log.Warning(ex, $"[PluginsServer] Socket error on port {currentPort}: {ex.Message} (attempt {retryCount + 1}/{maxRetries})");
                _server?.Dispose();
                _server = null;

                // If using a fixed port, don't retry
                if (ConstantTable.PluginsServerPort > 0)
                    break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[PluginsServer] Unexpected error starting PluginsServer on port {currentPort}");
                _server?.Dispose();
                _server = null;

                // If using a fixed port, don't retry
                if (ConstantTable.PluginsServerPort > 0)
                    break;
            }
        }

        if (!serverStarted)
        {
            Log.Error($"Failed to start PluginsServer after {maxRetries} attempts");
            _status = ServerStatus.Errored;
            return this;
        }

        _status = ServerStatus.Running;

        return this;
    }

    /// <summary>
    /// Finds a connection by connection ID
    /// </summary>
    /// <param name="connectionId">The connection ID</param>
    /// <returns>The plugin connection or null if not found</returns>
    public IPluginConnection? FindConnection(string connectionId) =>
        _connections.FirstOrDefault(x => x.ConnectionId?.Equals(connectionId) ?? false);

    /// <summary>
    /// Finds a connector for a specific plugin (implementation of IPluginServer)
    /// </summary>
    /// <param name="pluginInfo">The plugin info</param>
    /// <returns>The plugin connector or null if not found</returns>
    public IPluginConnector? FindConnector(PluginInfo pluginInfo)
    {
        // Use the existing FindConnection method and cast to IPluginConnector
        var connection = FindConnection(pluginInfo);
        return connection as IPluginConnector;
    }

    /// <summary>
    /// Finds a connection by plugin info
    /// </summary>
    /// <param name="pluginInfo">The plugin info</param>
    /// <returns>The plugin connection or null if not found</returns>
    public IPluginConnection? FindConnection(PluginInfo pluginInfo)
    {
        return _connections.FirstOrDefault(x => x.PluginInfo is not null && x.PluginInfo.Equals(pluginInfo));
    }

    /// <summary>
    /// Stops the plugin server (implementation of IPluginServer)
    /// </summary>
    public void Stop()
    {
        if (_status != ServerStatus.Running)
            return;

        _status = ServerStatus.Stopping;

        try
        {
            _server?.Dispose();
            _server = null;

            foreach (var connection in _connections)
            {
                connection.CloseAsync().Wait();
            }

            _connections.Clear();

            Log.Information("[PluginsServer] PluginsServer stopped");
            _status = ServerStatus.Pending;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginsServer] Error stopping PluginsServer");
            _status = ServerStatus.Errored;
        }
    }

    /// <summary>
    /// Closes the plugins server
    /// </summary>
    public async Task<PluginsServer> Close()
    {
        await CTask.Run(() =>
        {
            CTask.WaitAll(_connections.Select(c => c.CloseAsync()).ToArray());

            _connections.Clear();

            _server?.Dispose();

            _server = null;

            _status = ServerStatus.Pending;
        });

        return this;
    }

    /// <summary>
    /// Regular expression to verify connection ID (GUID format)
    /// </summary>
    private static Regex RegexToVerifyConnectionId() =>
        new(@"^[{]?[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}[}]?$");
}

/// <summary>
/// Plugin connection interface
/// </summary>
public interface IPluginConnection
{
    /// <summary>
    /// Gets the connection ID
    /// </summary>
    string? ConnectionId { get; }

    /// <summary>
    /// Gets or sets the plugin info
    /// </summary>
    PluginInfo? PluginInfo { get; set; }

    /// <summary>
    /// Gets the connection status
    /// </summary>
    ServerStatus Status { get; }

    /// <summary>
    /// Event raised when a message is received
    /// </summary>
    event EventHandler<string>? MessageReceived;

    /// <summary>
    /// Event raised when connection is closed
    /// </summary>
    event EventHandler? Closed;

    /// <summary>
    /// Initializes the connection
    /// </summary>
    void Initialize();

    /// <summary>
    /// Sends a message
    /// </summary>
    /// <param name="message">The message to send</param>
    void Send(string message);

    /// <summary>
    /// Closes the connection
    /// </summary>
    CTask CloseAsync();
}

/// <summary>
/// Plugin connection implementation
/// </summary>
public class PluginConnection : IPluginConnection, IPluginConnector
{
    private readonly IWebSocketConnection _connection;
    private ServerStatus _status = ServerStatus.Pending;

    /// <summary>
    /// Gets the connection ID
    /// </summary>
    public string? ConnectionId { get; private set; }

    /// <summary>
    /// IPluginConnector.ConnectionId — non-nullable explicit implementation
    /// </summary>
    string IPluginConnector.ConnectionId => ConnectionId!;

    /// <summary>
    /// Gets or sets the plugin info
    /// </summary>
    public PluginInfo? PluginInfo { get; set; }

    /// <summary>
    /// Gets the connection status
    /// </summary>
    public ServerStatus Status => _status;

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
    public event EventHandler<PluginResponseEventArgs>? PluginResponse;

    /// <summary>
    /// Event raised when plugin reports status (IPluginConnector implementation)
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<PluginStatusReportEventArgs>? StatusReport;
#pragma warning restore CS0067

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
            _status = ServerStatus.Running;
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
                        PluginResponse?.Invoke(this, new PluginResponseEventArgs
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
            _status = ServerStatus.Pending;
            Closed?.Invoke(this, EventArgs.Empty);
        };

        _connection.OnError = ex =>
        {
            _status = ServerStatus.Errored;
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
    public async CTask CloseAsync()
    {
        await CTask.Run(() =>
        {
            _connection.Close();
        });
    }
}

/// <summary>
/// Plugin connected event arguments
/// </summary>
public class PluginConnectedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the connection ID
    /// </summary>
    public string? ConnectionId { get; set; }
}

/// <summary>
/// Plugin disconnected event arguments
/// </summary>
public class PluginDisconnectedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the connection ID
    /// </summary>
    public string? ConnectionId { get; set; }
}

/// <summary>
/// Plugin message received event arguments
/// </summary>
public class PluginMessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the connection ID
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the message
    /// </summary>
    public string? Message { get; set; }
}
