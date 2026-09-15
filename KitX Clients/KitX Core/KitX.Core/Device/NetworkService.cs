using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Plugin;
using Serilog;

namespace KitX.Core.Device;

/// <summary>
/// Unified orchestrator for the device network stack (discovery UDP server,
/// device HTTP server, plugin WebSocket server). Owns startup ordering, port
/// configuration and shutdown. The Dashboard previously orchestrated these
/// servers directly in AppFramework's "Initialize WebManager" region; that
/// business logic now lives here, behind <see cref="INetworkService"/>.
///
/// <para>Port configuration: discovery + devices share
/// <c>UserSpecifiedDevicesServerPort</c>; plugins use
/// <c>UserSpecifiedPluginsServerPort</c>. <c>ConfigurePort</c> lives on the
/// concrete servers only — the casts below are Core-internal (same assembly),
/// so the contract interfaces stay free of configuration concerns.</para>
/// </summary>
public class NetworkService : INetworkService
{
    private readonly IConfigService _configService;
    private readonly IDeviceDiscoveryService _discoveryService;
    private readonly IDeviceServer _deviceServer;
    private readonly IPluginServer _pluginServer;
    private readonly DevicesOrganizer _devicesOrganizer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isRunning;

    /// <summary>
    /// Creates a new network orchestrator with dependency injection.
    /// <paramref name="devicesOrganizer"/> is injected to trigger its construction
    /// (it observes discovery events and manages device cards).
    /// </summary>
    public NetworkService(
        IConfigService configService,
        IDeviceDiscoveryService discoveryService,
        IDeviceServer deviceServer,
        IPluginServer pluginServer,
        DevicesOrganizer devicesOrganizer)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _deviceServer = deviceServer ?? throw new ArgumentNullException(nameof(deviceServer));
        _pluginServer = pluginServer ?? throw new ArgumentNullException(nameof(pluginServer));
        _devicesOrganizer = devicesOrganizer ?? throw new ArgumentNullException(nameof(devicesOrganizer));
    }

    /// <inheritdoc/>
    public bool IsRunning => _isRunning;

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct = default)
    {
        var config = _configService.AppConfig;

        // Startup ordering: honor the configured delay before binding any socket,
        // and respect the --disable-network-system startup switch.
        var delayMs = Convert.ToInt32(config.Web.DelayStartSeconds * 1000);
        if (delayMs > 0)
            await Task.Delay(delayMs, ct);

        if (ConstantTable.SkipNetworkSystemOnStartup)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            var devicesPort = (int)(config.Web.UserSpecifiedDevicesServerPort ?? 0);
            var pluginsPort = (int)(config.Web.UserSpecifiedPluginsServerPort ?? 0);

            if (_discoveryService is DevicesDiscoveryServer discoveryServer)
            {
                discoveryServer.ConfigurePort(devicesPort);
                discoveryServer.Run();
            }

            // DevicesOrganizer observes discovery events — its construction is
            // triggered by the ctor dependency above.
            _ = _devicesOrganizer;

            if (_deviceServer is DevicesServer devicesServer)
            {
                devicesServer.ConfigurePort(devicesPort);
                devicesServer.Run();
            }

            if (_pluginServer is PluginsServer pluginsServer)
            {
                Log.Information("[NetworkService] About to call PluginsServer.Run(). PluginsServer HashCode: {HashCode}", pluginsServer.GetHashCode());
                pluginsServer.ConfigurePort(pluginsPort);
                pluginsServer.Run();
            }

            _isRunning = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _pluginServer.Stop();
            _deviceServer.Stop();
            _discoveryService.Stop();
            _isRunning = false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RestartDevicesServersAsync(CancellationToken ct = default)
    {
        // Let UDP sockets release before re-binding (mirrors the old Dashboard flow).
        var settleDelay = _configService.AppConfig.Web.UdpSendFrequency + 200;

        await _lock.WaitAsync(ct);
        try
        {
            _deviceServer.Stop();
            _discoveryService.Stop();

            await Task.Delay(settleDelay, ct);

            var devicesPort = (int)(_configService.AppConfig.Web.UserSpecifiedDevicesServerPort ?? 0);

            if (_discoveryService is DevicesDiscoveryServer discoveryServer)
            {
                discoveryServer.ConfigurePort(devicesPort);
                discoveryServer.Run();
            }

            if (_deviceServer is DevicesServer devicesServer)
            {
                devicesServer.ConfigurePort(devicesPort);
                devicesServer.Run();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopDevicesServersAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _deviceServer.Stop();
            _discoveryService.Stop();
        }
        finally
        {
            _lock.Release();
        }
    }
}
