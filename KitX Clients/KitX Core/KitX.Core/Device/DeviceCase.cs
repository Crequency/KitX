using KitX.Core.Configuration;
using System;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Security;
using KitX.Core.Security;
using KitX.Shared.CSharp.Device;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.Core.Device;

/// <summary>
/// Device case implementation
/// Phase 6.5: Aligned with legacy DeviceCase functionality
/// </summary>
public class DeviceCase : IDeviceCase
{
    private readonly IConfigService _configService;
    private readonly IDeviceKeyService _securityService;
    private readonly DevicesServer _devicesServer;

    /// <summary>
    /// Creates a new device case
    /// </summary>
    public DeviceCase() : this(new DeviceInfo()) { }

    /// <summary>
    /// Creates a new device case with device info
    /// </summary>
    /// <param name="deviceInfo">Device information</param>
    public DeviceCase(DeviceInfo deviceInfo)
    {
        DeviceInfo = deviceInfo;
        _configService = ConfigManager.Instance;
        _securityService = SecurityManager.Instance;
        _devicesServer = DevicesServer.Instance;
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
    public bool IsCurrentDevice => DeviceInfo.IsCurrentDevice(DevicesDiscoveryServer.Instance.DefaultDeviceInfo);

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
