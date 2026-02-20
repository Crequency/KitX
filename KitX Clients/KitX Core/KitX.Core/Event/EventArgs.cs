using System;

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
    public KitX.Shared.CSharp.Device.DeviceInfo? DeviceInfo { get; set; }
}
