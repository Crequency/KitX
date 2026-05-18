using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Security;
using KitX.Shared.CSharp.Device;

namespace KitX.Core.Device;

/// <summary>
/// Device case implementation
/// Phase 6.5: Aligned with legacy DeviceCase functionality
/// </summary>
public class DeviceCase : IDeviceCase
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
    /// <param name="configService">Configuration service</param>
    /// <param name="securityService">Security service</param>
    /// <param name="devicesServer">Devices server</param>
    /// <param name="deviceDiscoveryService">Device discovery service</param>
    public DeviceCase(DeviceInfo deviceInfo, IConfigService configService, IDeviceKeyService securityService, IDeviceServer devicesServer, IDeviceDiscoveryService deviceDiscoveryService)
    {
        DeviceInfo = deviceInfo;
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _devicesServer = devicesServer ?? throw new ArgumentNullException(nameof(devicesServer));
        _deviceDiscoveryService = deviceDiscoveryService ?? throw new ArgumentNullException(nameof(deviceDiscoveryService));
    }

    /// <inheritdoc/>
    public DeviceInfo DeviceInfo { get; set; }

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
