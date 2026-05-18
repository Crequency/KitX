using KitX.Shared.CSharp.Plugin;

namespace KitX.Core.Event;

/// <summary>
/// Event args for port changed events
/// </summary>
public class PortChangedEventArgs : EventArgs
{
    /// <summary>
    /// The new port number
    /// </summary>
    public int Port { get; set; }
}

/// <summary>
/// Event args for device key events
/// </summary>
public class DeviceKeyEventArgs : EventArgs
{
    /// <summary>
    /// The device key
    /// </summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Event args for device info events
/// </summary>
public class DeviceInfoEventArgs : EventArgs
{
    /// <summary>
    /// The device information
    /// </summary>
    public Shared.CSharp.Device.DeviceInfo? DeviceInfo { get; set; }
}

/// <summary>
/// Event args for plugin events
/// </summary>
[Obsolete("Use KitX.Core.Contract.Plugin.Events.PluginRegisteredEventArgs or PluginUnregisteredEventArgs instead.")]
public class PluginEventArgs : EventArgs
{
    /// <summary>
    /// The plugin information
    /// </summary>
    public PluginInfo? PluginInfo { get; set; }
}

/// <summary>
/// Event args for plugin connection events
/// </summary>
public class PluginConnectionEventArgs : EventArgs
{
    /// <summary>
    /// The connection ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// The plugin information
    /// </summary>
    public PluginInfo? PluginInfo { get; set; }
}

/// <summary>
/// Event args for exchange device key request events.
/// Published when a key exchange request is received, requiring user confirmation.
/// </summary>
public class ExchangeDeviceKeyEventArgs : EventArgs
{
    /// <summary>
    /// The verification code displayed to the user for confirmation.
    /// This code must match between the two devices for the exchange to proceed.
    /// </summary>
    public string VerificationCode { get; set; } = string.Empty;

    /// <summary>
    /// The address of the requesting device
    /// </summary>
    public string RequestingDeviceAddress { get; set; } = string.Empty;

    /// <summary>
    /// The encrypted device key from the request (for informational purposes only, not for UI display)
    /// </summary>
    public string? EncryptedDeviceKey { get; set; }
}
