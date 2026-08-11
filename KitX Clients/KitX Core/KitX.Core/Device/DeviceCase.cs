using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Input;
using KitX.Core.Common;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Security;
using KitX.Shared.CSharp.Device;
using Serilog;

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
    private readonly IDeviceConnectionClient _connectionClient;
    private readonly IDeviceKeyExchangeUi _keyExchangeUi;

    /// <summary>
    /// Creates a new device case with dependency injection
    /// </summary>
    public DeviceCase(
        IConfigService configService,
        IDeviceKeyService securityService,
        IDeviceServer devicesServer,
        IDeviceDiscoveryService deviceDiscoveryService,
        IDeviceConnectionClient connectionClient,
        IDeviceKeyExchangeUi keyExchangeUi)
        : this(new DeviceInfo(), configService, securityService, devicesServer, deviceDiscoveryService,
              connectionClient, keyExchangeUi)
    {
    }

    /// <summary>
    /// Creates a new device case with device info and dependency injection
    /// </summary>
    public DeviceCase(
        DeviceInfo deviceInfo,
        IConfigService configService,
        IDeviceKeyService securityService,
        IDeviceServer devicesServer,
        IDeviceDiscoveryService deviceDiscoveryService,
        IDeviceConnectionClient connectionClient,
        IDeviceKeyExchangeUi keyExchangeUi)
    {
        DeviceInfo = deviceInfo;
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _devicesServer = devicesServer ?? throw new ArgumentNullException(nameof(devicesServer));
        _deviceDiscoveryService = deviceDiscoveryService ?? throw new ArgumentNullException(nameof(deviceDiscoveryService));
        _connectionClient = connectionClient ?? throw new ArgumentNullException(nameof(connectionClient));
        _keyExchangeUi = keyExchangeUi ?? throw new ArgumentNullException(nameof(keyExchangeUi));

        AuthorizeAndExchangeDeviceKeyCommand = new AsyncRelayCommand(AuthorizeAndExchangeDeviceKeyAsync);
        UnAuthorizeCommand = new AsyncRelayCommand(UnAuthorizeAsync);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

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
    /// Initiates the encrypted key exchange and authenticated connection against the
    /// target device (initiating side). Displays the temporary password, exchanges
    /// public keys, then connects to obtain a session token.
    /// </summary>
    public ICommand AuthorizeAndExchangeDeviceKeyCommand { get; }

    /// <summary>
    /// Removes this device's key, revoking authorization.
    /// </summary>
    public ICommand UnAuthorizeCommand { get; }

    private async Task AuthorizeAndExchangeDeviceKeyAsync()
    {
        if (IsCurrentDevice)
            return;

        var password = GeneratePassword();

        try
        {
            using (_keyExchangeUi.ShowPasswordForInitiator(password))
            {
                var result = await _connectionClient.ExchangeKeyAsync(DeviceInfo, password);
                if (!result.Success)
                {
                    Log.Warning("[DeviceCase] Key exchange failed for {Device}: {Error}",
                        DeviceInfo.Device.DeviceName, result.Error);
                    return;
                }
            }

            var token = await _connectionClient.ConnectAsync(DeviceInfo);
            if (token is not null)
                ConnectionToken = token;

            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DeviceCase] AuthorizeAndExchangeDeviceKeyAsync failed for {Device}",
                DeviceInfo.Device.DeviceName);
        }
    }

    private async Task UnAuthorizeAsync()
    {
        await Task.Yield();

        try
        {
            _securityService.RemoveDeviceKey(DeviceInfo.Device.MacAddress);
            ConnectionToken = null;
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DeviceCase] UnAuthorizeAsync failed for {Device}", DeviceInfo.Device.DeviceName);
        }
    }

    /// <summary>
    /// Generates an 8-digit temporary password using digits 1-9 (matching the receive-side
    /// entry regex <c>[1-9]{8}</c>).
    /// </summary>
    private static string GeneratePassword()
    {
        const string digits = "123456789";
        var bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[8];
        for (var i = 0; i < chars.Length; ++i)
            chars[i] = digits[bytes[i] % digits.Length];
        return new string(chars);
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsAuthorized));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectionToken));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Checks if the device is offline (not seen within TTL period)
    /// </summary>
    private bool IsOffline()
    {
        var ttl = TimeSpan.FromSeconds(_configService.AppConfig.Web.DeviceInfoTTLSeconds);
        return DeviceInfo.IsOffline(_configService.AppConfig.Web.DeviceInfoTTLSeconds);
    }
}
