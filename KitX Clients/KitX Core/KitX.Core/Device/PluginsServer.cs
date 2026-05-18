using System.Text.Json;
using System.Text.RegularExpressions;
using Fleck;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Core.Contract.Event;
using KitX.Core.Event;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using Serilog;
using CTask = System.Threading.Tasks.Task;
using IPluginConnection = KitX.Core.Contract.Plugin.IPluginConnection;
using PluginConnectedEventArgs = KitX.Core.Contract.Plugin.Events.PluginConnectedEventArgs;
using PluginDisconnectedEventArgs = KitX.Core.Contract.Plugin.Events.PluginDisconnectedEventArgs;
using PluginMessageReceivedEventArgs = KitX.Core.Contract.Plugin.Events.PluginMessageReceivedEventArgs;

namespace KitX.Core.Device;

/// <summary>
/// Plugins server for WebSocket connections
/// </summary>
public class PluginsServer : ServerBase, IPluginServer
{
    private readonly IEventService _eventService;
    private WebSocketServer? _server;
    private readonly List<KitX.Core.Contract.Plugin.IPluginConnection> _connections = new();

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
    /// Gets or sets the port
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    /// Configured port for the server
    /// </summary>
    private int? _configuredPort;

    /// <summary>
    /// Configures the port for the server
    /// </summary>
    /// <param name="port">The port number</param>
    public void ConfigurePort(int port)
    {
        _configuredPort = port is >= 0 and <= 65535 ? port : null;
    }

    /// <summary>
    /// Event raised when server port changes
    /// </summary>
#pragma warning disable CS0067
    public event EventHandler<int>? PortChanged;
#pragma warning restore CS0067

    /// <summary>
    /// IPluginServer.Connections — returns connected plugins as IPluginConnection list
    /// </summary>
    IReadOnlyList<IPluginConnection> IPluginServer.Connections =>
        _connections.ToList().AsReadOnly();

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
    /// Creates a new plugins server with dependency injection
    /// </summary>
    /// <param name="eventService">The event service for publishing events</param>
    public PluginsServer(IEventService eventService)
    {
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        Log.Information("[PluginsServer] Constructor called. HashCode: {HashCode}, EventService HashCode: {EventServiceHashCode}",
            GetHashCode(), eventService.GetHashCode());
    }

    /// <summary>
    /// Runs the plugins server with retry logic for port conflicts
    /// </summary>
    /// <returns>The server instance</returns>
    public IPluginServer Run()
    {
        if (!TryStart())
            return this;

        const int maxRetries = 10;
        const int defaultBasePort = 7777;

        int basePort = defaultBasePort;
        if (_configuredPort > 0)
            basePort = _configuredPort.Value;
        else if (ConstantTable.PluginsServerPort > 0)
            basePort = ConstantTable.PluginsServerPort;

        bool serverStarted = false;

        int currentPort = basePort;
        StartServer(ref serverStarted, ref currentPort);

        for (int retryCount = 1; retryCount < maxRetries && !serverStarted; retryCount++)
        {
            currentPort = basePort + retryCount;
            StartServer(ref serverStarted, ref currentPort);
        }

        if (!serverStarted)
        {
            Log.Warning("[PluginsServer] All sequential port attempts failed, falling back to system-assigned port (0)");
            currentPort = 0;
            StartServer(ref serverStarted, ref currentPort);
        }

        if (!serverStarted)
        {
            Log.Error($"Failed to start PluginsServer after {maxRetries} sequential attempts and system-assigned port fallback");
            SetErrored(null, nameof(PluginsServer));
            return this;
        }

        SetRunning();

        return this;
    }

    private void StartServer(ref bool serverStarted, ref int currentPort)
    {
        try
        {
            _server = new WebSocketServer($"ws://0.0.0.0:{currentPort}");

            _server!.Start(socket =>
            {
                var connectionId = socket.ConnectionInfo.Path.Trim('/');

                if (RegexToVerifyConnectionId().IsMatch(connectionId) == false)
                {
                    socket.Send("Invalid connection id.");
                    socket.Close();
                    return;
                }

                Log.Information($"[PluginsServer] About to add connection {connectionId}. _connections count before: {_connections.Count}, this HashCode: {GetHashCode()}");
                var connection = new PluginConnection(socket, connectionId);
                _connections.Add(connection);
                Log.Information($"[PluginsServer] Added connection {connectionId}. _connections count after: {_connections.Count}");

                connection.Closed += (sender, args) =>
                {
                    _connections.Remove(connection);

                    Log.Information($"[PluginsServer] Connection closed: {connectionId}, PluginInfo: {connection.PluginInfo?.Name}");

                    if (connection.PluginInfo is not null)
                    {
                        Log.Information($"[PluginsServer] Publishing PluginUnregistered for: {connection.PluginInfo.Name}");

                        _eventService.Publish(EventNames.PluginUnregistered, new PluginUnregisteredEventArgs
                        {
                            PluginInfo = connection.PluginInfo
                        });
                    }

                    Log.Information($"[PluginsServer] Publishing PluginDisconnected for: {connectionId}");

                    _eventService.Publish(EventNames.PluginDisconnected, new PluginConnectionEventArgs
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
                            Log.Information($"[PluginsServer] MessageReceived: cmd.Request = {cmd.Request}, expected = {KitX.Shared.CSharp.WebCommand.Infos.CommandRequestInfo.RegisterPlugin}");
                            if (cmd.Request == KitX.Shared.CSharp.WebCommand.Infos.CommandRequestInfo.RegisterPlugin)
                            {
                                Log.Information($"[PluginsServer] Processing RegisterPlugin message");
                                var body = System.Text.Encoding.UTF8.GetString(cmd.Body.AsSpan(0, cmd.BodyLength).ToArray());
                                var pluginInfo = System.Text.Json.JsonSerializer.Deserialize<PluginInfo>(body);
                                if (pluginInfo is not null)
                                {
                                    pluginInfo.Tags ??= new();
                                    pluginInfo.Tags[nameof(PluginConnection.ConnectionId)] = connectionId;
                                    pluginInfo.Tags["JoinTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss(FF)");
                                    connection.PluginInfo = pluginInfo;
                                    Log.Information($"[PluginsServer] Publishing PluginRegistered event for: {pluginInfo.Name}");
                                    _eventService.Publish(EventNames.PluginRegistered, new PluginRegisteredEventArgs
                                    {
                                        PluginInfo = pluginInfo
                                    });

                                    try
                                    {
                                        if (DI.ServiceHost.IsInitialized)
                                        {
                                            var pluginsManager = (Plugin.PluginsManager)DI.ServiceHost.GetRequiredService<IPluginService>();
                                            pluginsManager.OnPluginStatusChanged(pluginInfo.Name, PluginStatus.Running);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Warning(ex, "[PluginsServer] Failed to notify Running status after registration for {PluginName}",
                                            pluginInfo.Name);
                                    }
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

                connection.PluginResponse += (sender, args) =>
                {
                    Log.Information($"[PluginsServer] Publishing PluginResponse event, RequestId: {args.RequestId}");
                    _eventService.Publish(EventNames.PluginResponse, args);
                };

                connection.StatusReport += (sender, args) =>
                {
                    try
                    {
                        var conn = sender as PluginConnection ?? connection;
                        var pluginName = conn.PluginInfo?.Name;
                        if (pluginName is null)
                        {
                            Log.Debug("[PluginsServer] StatusReport received but plugin not yet registered, ignoring (ConnectionId: {ConnectionId})", args.ConnectionId);
                            return;
                        }

                        var newStatus = args.Status switch
                        {
                            "Running" => PluginStatus.Running,
                            "Pending" => PluginStatus.Stopped,
                            "Errored" => PluginStatus.Error,
                            _ => PluginStatus.Unknown
                        };

                        Log.Information("[PluginsServer] Plugin '{PluginName}' status changed to {Status} (ConnectionId: {ConnectionId})",
                            pluginName, newStatus, args.ConnectionId);

                        if (DI.ServiceHost.IsInitialized)
                        {
                            var pluginsManager = (Plugin.PluginsManager)DI.ServiceHost.GetRequiredService<IPluginService>();
                            pluginsManager.OnPluginStatusChanged(pluginName, newStatus);
                        }
                        else
                        {
                            Log.Error("[PluginsServer] Cannot forward status change: ServiceHost not initialized");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[PluginsServer] Error forwarding StatusReport to PluginsManager");
                    }
                };

                connection.Initialize();

                _eventService.Publish(EventNames.PluginConnected, new PluginConnectedEventArgs
                {
                    ConnectionId = connectionId
                });
            });

            Port = _server!.Port;
            serverStarted = true;

            ConstantTable.PluginsServerPort = Port ?? 0;

            Log.Information($"[PluginsServer] PluginsServer started on port {Port}");

            _eventService.Publish(EventNames.PluginsServerPortChanged, new PortChangedEventArgs { Port = Port ?? 0 });
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            Log.Warning(ex, $"[PluginsServer] Socket error on port {currentPort}: {ex.Message}");
            _server?.Dispose();
            _server = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[PluginsServer] Unexpected error starting PluginsServer on port {currentPort}");
            _server?.Dispose();
            _server = null;
        }
    }

    /// <summary>
    /// Finds a connection by connection ID
    /// </summary>
    /// <param name="connectionId">The connection ID</param>
    /// <returns>The plugin connection or null if not found</returns>
    public KitX.Core.Contract.Plugin.IPluginConnection? FindConnection(string connectionId) =>
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
    public KitX.Core.Contract.Plugin.IPluginConnection? FindConnection(PluginInfo pluginInfo)
    {
        return _connections.FirstOrDefault(x => x.PluginInfo is not null && x.PluginInfo.Equals(pluginInfo));
    }

    /// <summary>
    /// Stops the plugin server (implementation of IPluginServer)
    /// </summary>
    public void Stop()
    {
        if (!TryStop())
            return;

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
            SetPending();
        }
        catch (Exception ex)
        {
            SetErrored(ex, nameof(PluginsServer));
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

            SetPending();
        });

        return this;
    }

    /// <summary>
    /// Regular expression to verify connection ID (GUID format)
    /// </summary>
    private static Regex RegexToVerifyConnectionId() =>
        new(@"^[{]?[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}[}]?$");
}
