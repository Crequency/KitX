namespace KitX.Core.Device.Events;

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