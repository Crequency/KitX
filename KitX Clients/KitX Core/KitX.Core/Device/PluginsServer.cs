using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fleck;
using KitX.Core.Contract.Plugin;
using KitX.Shared.CSharp.Plugin;
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
    public event EventHandler<int>? PortChanged;

    /// <summary>
    /// Gets the list of plugin connections
    /// </summary>
    public IReadOnlyList<IPluginConnection> Connections => _connections.AsReadOnly();

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
    /// Private constructor
    /// </summary>
    private PluginsServer() { }

    /// <summary>
    /// Initializes the server
    /// </summary>
    private void InitializeServer()
    {
        var port = 7777;

        port = port is >= 0 and <= 65535 ? port : 0;

        _server ??= new WebSocketServer($"ws://0.0.0.0:{port}");
    }

    /// <summary>
    /// Runs the plugins server
    /// </summary>
    /// <returns>The server instance</returns>
    public IPluginServer Run()
    {
        if (_status != ServerStatus.Pending)
            return this;

        _status = ServerStatus.Starting;

        InitializeServer();

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

            connection.MessageReceived += (sender, message) =>
            {
                try
                {
                    var kwc = System.Text.Json.JsonSerializer.Deserialize<KitX.Shared.CSharp.WebCommand.Request>(message);
                    if (kwc?.Content is not null)
                    {
                        var cmd = System.Text.Json.JsonSerializer.Deserialize<KitX.Shared.CSharp.WebCommand.Command>(kwc.Content);
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
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error handling plugin message");
                }

                PluginMessageReceived?.Invoke(this, new PluginMessageReceivedEventArgs
                {
                    ConnectionId = connectionId,
                    Message = message
                });
            };

            connection.Closed += (sender, args) =>
            {
                _connections.Remove(connection);

                // Trigger PluginUnregistered if this connection had a registered plugin
                if (connection.PluginInfo is not null)
                {
                    PluginUnregistered?.Invoke(this, new PluginUnregisteredEventArgs
                    {
                        PluginInfo = connection.PluginInfo
                    });
                }

                PluginDisconnected?.Invoke(this, new PluginDisconnectedEventArgs
                {
                    ConnectionId = connectionId
                });
            };

            connection.Initialize();

            PluginConnected?.Invoke(this, new PluginConnectedEventArgs
            {
                ConnectionId = connectionId
            });
        });

        Port = _server!.Port;

        Log.Information($"PluginsServer started on port {Port}");

        PortChanged?.Invoke(this, Port ?? 0);

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
        // Return null for now - actual implementation would find by plugin info
        return null;
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

            Log.Information("PluginsServer stopped");
            _status = ServerStatus.Pending;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping PluginsServer");
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
public class PluginConnection : IPluginConnection
{
    private readonly IWebSocketConnection _connection;
    private ServerStatus _status = ServerStatus.Pending;

    /// <summary>
    /// Gets the connection ID
    /// </summary>
    public string? ConnectionId { get; private set; }

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
            Serilog.Log.Error(ex, $"PluginConnection error for {ConnectionId}");
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
