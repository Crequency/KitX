using System;
using KitX.Shared.CSharp.Plugin;
using CTask = System.Threading.Tasks.Task;

namespace KitX.Core.Device;

/// <summary>
/// Plugin connection interface
/// </summary>
public interface IPluginConnection
{
    /// <summary>
    /// Gets the connection ID
    /// </summary>
    string? ConnectionId { get; }

    /// <summary>
    /// Gets or sets the plugin info
    /// </summary>
    PluginInfo? PluginInfo { get; set; }

    /// <summary>
    /// Gets the connection status
    /// </summary>
    ServerStatus Status { get; }

    /// <summary>
    /// Event raised when a message is received
    /// </summary>
    event EventHandler<string>? MessageReceived;

    /// <summary>
    /// Event raised when connection is closed
    /// </summary>
    event EventHandler? Closed;

    /// <summary>
    /// Initializes the connection
    /// </summary>
    void Initialize();

    /// <summary>
    /// Sends a message
    /// </summary>
    /// <param name="message">The message to send</param>
    void Send(string message);

    /// <summary>
    /// Closes the connection
    /// </summary>
    CTask CloseAsync();
}