using System.ComponentModel;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Security;
using KitX.Shared.CSharp.Device;

namespace KitX.Core.Device;

/// <summary>
/// Device case implementation
/// Phase 6.5: Aligned with legacy DeviceCase functionality
/// </summary>
public class DeviceCase : IDeviceCase, INotifyPropertyChanged
{
    private readonly IConfigService _configService;
    private readonly IDeviceKeyService _securityService;
    private readonly IDeviceServer _devicesServer;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    /// <summary>
    /// Creates a new device case with dependency injection
    /// </summary>
    public DeviceCase(IConfigService configService, IDeviceKeyService securityService, IDeviceServer devicesServer, IDeviceDiscoveryService deviceDiscoveryService)
        : this(new DeviceInfo(), configService, securityService, devicesServer, deviceDiscoveryService)
    {
    }

    /// <summary>
    /// Creates a new device case with device info and dependency injection
    /// </summary>
    /// <param name="deviceInfo">Device information</param>
    /// <param name="configService">The config service</param>
    /// <param name="securityService">The security service</param>
    /// <param name="devicesServer">The devices server</param>
    /// <param name="deviceDiscoveryService">The device discovery service</param>
    public DeviceCase(DeviceInfo deviceInfo, IConfigService configService, IDeviceKeyService securityService, IDeviceServer devicesServer, IDeviceDiscoveryService deviceDiscoveryService)
    {
        DeviceInfo = deviceInfo;
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _devicesServer = devicesServer ?? throw new ArgumentNullException(nameof(devicesServer));
        _deviceDiscoveryService = deviceDiscoveryService ?? throw new ArgumentNullException(nameof(deviceDiscoveryService));
    }

    /// <summary>
    /// Raised when a bound property changes. The DevicesPage card binds
    /// <c>DeviceInfo.*</c> chains; without this, refreshed discovery broadcasts
    /// (new PluginsCount/SendTime) replace the backing object silently and the
    /// card freezes at its first-render values.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private DeviceInfo? _deviceInfo;

    /// <inheritdoc/>
    public DeviceInfo DeviceInfo
    {
        get => _deviceInfo!;
        set
        {
            _deviceInfo = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeviceInfo)));
        }
    }

    /// <inheritdoc/>
    public bool IsAuthorized => _securityService.IsDeviceAuthorized(DeviceInfo.Device);

    /// <inheritdoc/>
    public bool IsMainDevice => DeviceInfo?.IsMainDevice ?? false;

    /// <inheritdoc/>
    public bool IsOnline => !IsOffline();

    /// <inheritdoc/>
    public DateTime LastSeen => DeviceInfo?.SendTime ?? DateTime.UtcNow;

    /// <summary>
    /// Gets a value indicating whether the device is currently signed in
    /// </summary>
    public bool IsConnected => _devicesServer.IsDeviceSignedIn(DeviceInfo.Device) || ConnectionToken is not null;

    /// <summary>
    /// Gets a value indicating whether this is the current device
    /// </summary>
    public bool IsCurrentDevice => DeviceInfo.IsCurrentDevice(_deviceDiscoveryService.DefaultDeviceInfo);

    /// <summary>
    /// Connection token for authenticated communication
    /// </summary>
    public string? ConnectionToken { get; set; }

    /// <summary>
    /// Checks if the device is offline (not seen within TTL period)
    /// </summary>
    private bool IsOffline()
    {
        var ttl = TimeSpan.FromSeconds(_configService.AppConfig.Web.DeviceInfoTTLSeconds);
        return DeviceInfo.IsOffline(_configService.AppConfig.Web.DeviceInfoTTLSeconds);
    }
}
