using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Fleck;
using KitX.Core.Device;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Details;
using KitX.Shared.CSharp.WebCommand.Infos;
using Serilog;
using CTask = System.Threading.Tasks.Task;

namespace KitX.Core.Plugin;

/// <summary>
/// Plugin connector for managing plugin WebSocket connections
/// Phase 5: Decoupled from UI ViewInstances, uses events instead
/// </summary>
public class PluginConnector
{
    private readonly IWebSocketConnection? _connection;
    private readonly IWebSocketConnectionInfo? _connectionInfo;
    private string? _path;
    private bool _initialized = false;
    private PluginInfo? _pluginInfo;
    private ServerStatus _connectorStatus = ServerStatus.Pending;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Plugin status updated event
    /// </summary>
    public event Action? PluginStatusUpdated;

    /// <summary>
    /// Plugin response event - when receiving plugin function call responses
    /// </summary>
    public static event Action<string, string>? OnPluginResponse;

    /// <summary>
    /// Event raised when a plugin is registered
    /// UI layer should subscribe to this to update ViewInstances.PluginInfos
    /// </summary>
    public event EventHandler<PluginRegisteredEventArgs>? PluginRegistered;

    /// <summary>
    /// Event raised when a plugin is unregistered/disconnected
    /// UI layer should subscribe to this to update ViewInstances.PluginInfos
    /// </summary>
    public event EventHandler<PluginUnregisteredEventArgs>? PluginUnregistered;

    public PluginConnector() { }

    public PluginConnector(IWebSocketConnection socket)
    {
        _connection = socket;
        _connectionInfo = socket.ConnectionInfo;
    }

    /// <summary>
    /// Gets or sets the plugin path
    /// </summary>
    public string? Path
    {
        get => _path;
        set
        {
            _path = value;
            PluginStatusUpdated?.Invoke();
        }
    }

    /// <summary>
    /// Gets the connection ID
    /// </summary>
    public string? ConnectionId => Path;

    /// <summary>
    /// Gets or sets the plugin info
    /// </summary>
    public PluginInfo? PluginInfo
    {
        get => _pluginInfo;
        set
        {
            _pluginInfo = value;
            PluginStatusUpdated?.Invoke();
        }
    }

    /// <summary>
    /// Gets a value indicating whether plugin info is available
    /// </summary>
    public bool PluginInfoAvailable => PluginInfo is null;

    /// <summary>
    /// Gets or sets the connector status
    /// </summary>
    public ServerStatus ConnectorStatus
    {
        get => _connectorStatus;
        set
        {
            _connectorStatus = value;
            PluginStatusUpdated?.Invoke();
        }
    }

    /// <summary>
    /// Initializes the connector
    /// </summary>
    public PluginConnector Initialize()
    {
        _initialized = true;
        Path = _connectionInfo!.Path.Trim('/');
        return this;
    }

    /// <summary>
    /// Runs the connector
    /// </summary>
    public PluginConnector Run()
    {
        if (_initialized == false)
            Initialize();

        const string location = $"{nameof(PluginConnector)}.{nameof(Run)}";

        _connection!.OnOpen = () => { };

        _connection.OnClose = () =>
        {
            try
            {
                if (PluginInfo is not null)
                {
                    // Trigger event instead of directly removing from ViewInstances
                    PluginUnregistered?.Invoke(this, new PluginUnregisteredEventArgs
                    {
                        PluginInfo = PluginInfo
                    });
                }
            }
            catch (Exception e)
            {
                Log.Warning(e, $"In {location}: {e.Message}");
            }

            // TODO: Remove from PluginsServer.PluginConnectors
        };

        _connection.OnMessage = message =>
        {
            var kwc = JsonSerializer.Deserialize<Request>(message, _serializerOptions);

            if (kwc is null)
                return;

            var command = JsonSerializer.Deserialize<Command>(kwc.Content, _serializerOptions);

            // Command is a value type, so it can't be null, but we check deserialization success
            if (command.Tags != null &&
                command.Tags.TryGetValue("RequestId", out var requestId))
            {
                // Trigger plugin response event
                OnPluginResponse?.Invoke(requestId, kwc.Content);
                return;
            }

            HandleCommand(command, kwc);
        };

        _connection.OnError = ex =>
        {
            try
            {
                if (PluginInfo is not null)
                {
                    // Trigger event instead of directly removing from ViewInstances
                    PluginUnregistered?.Invoke(this, new PluginUnregisteredEventArgs
                    {
                        PluginInfo = PluginInfo
                    });
                }
            }
            catch (Exception e)
            {
                Log.Warning(e, $"In {location}: {e.Message}");
            }

            // TODO: Remove from PluginsServer.PluginConnectors

            Log.Error(ex, $"In {location}: {ex.Message}");
        };

        return this;
    }

    /// <summary>
    /// Handles incoming commands from the plugin
    /// </summary>
    private void HandleCommand(Command command, Request request)
    {
        const string location = $"{nameof(PluginConnector)}.{nameof(HandleCommand)}";

        switch (command.Request)
        {
            case CommandRequestInfo.RegisterPlugin:
                var body = Encoding.UTF8.GetString(command.Body.AsSpan(0, command.BodyLength).ToArray());
                PluginInfo = JsonSerializer.Deserialize<PluginInfo>(body, _serializerOptions);

                ArgumentNullException.ThrowIfNull(PluginInfo, nameof(PluginInfo));
                ArgumentNullException.ThrowIfNull(PluginInfo.Tags, nameof(PluginInfo.Tags));

                PluginInfo.Tags.Add(nameof(ConnectionId), ConnectionId ?? string.Empty);
                PluginInfo.Tags.Add("JoinTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss(FF)"));

                // Trigger event instead of directly adding to ViewInstances
                PluginRegistered?.Invoke(this, new PluginRegisteredEventArgs
                {
                    PluginInfo = PluginInfo
                });

                Log.Information($"In {location}: New plugin registered with {body.Replace("\r", "").Replace("\n", "")}");
                break;

            case CommandRequestInfo.RequestWorkingDetail:
                SendWorkingDetail();
                break;

            case CommandRequestInfo.ReportStatus:
                // TODO: Handle status report
                break;

            case CommandRequestInfo.RequestCommand:
                // TODO: Handle command request
                break;
        }
    }

    /// <summary>
    /// Sends a message
    /// </summary>
    private void SendMessage<T>(T content) => _connection!.Send(JsonSerializer.Serialize(content, _serializerOptions));

    /// <summary>
    /// Sends working detail
    /// </summary>
    private void SendWorkingDetail()
    {
        if (_path is null or { Length: 0 })
        {
            SendMessage(new PluginWorkingDetail
            {
                PluginDataDirectory = null,
                PluginSaveDirectory = null
            });
        }
    }

    /// <summary>
    /// Sends a request to the plugin
    /// </summary>
    public async void Request(Request request)
    {
        await _connection!.Send(JsonSerializer.Serialize(request, _serializerOptions));
    }

    /// <summary>
    /// Closes the connector
    /// </summary>
    public async Task<PluginConnector> CloseAsync()
    {
        await CTask.Run(() =>
        {
            _connection!.Close();
        });

        return this;
    }
}

/// <summary>
/// Plugin registered event arguments
/// </summary>
public class PluginRegisteredEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the plugin info
    /// </summary>
    public PluginInfo? PluginInfo { get; set; }
}

/// <summary>
/// Plugin unregistered event arguments
/// </summary>
public class PluginUnregisteredEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the plugin info
    /// </summary>
    public PluginInfo? PluginInfo { get; set; }
}
