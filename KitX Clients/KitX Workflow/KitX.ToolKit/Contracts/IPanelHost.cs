using System.Text.Json;
using KitX.ToolKit.Models;

namespace KitX.ToolKit.Contracts;

/// <summary>
/// A filtered panel state change delivered to a renderer (desktop = Avalonia adapter;
/// remote = WS broadcaster in M5). Derived from <c>DataStore.Changed</c> for the bound keys.
/// </summary>
public sealed record PanelStateChange(string InstanceId, string ControlId, string Prop, JsonElement? Value);

/// <summary>A user interaction with a control (upstream, edge-triggered).</summary>
public sealed record ControlInteractedEventArgs(string InstanceId, string ControlId, string EventName, object? Value);

/// <summary>
/// Panel renderer (frontend side): desktop = Avalonia renderer; remote = WS broadcaster (M5).
/// The backend pushes filtered state changes; the frontend reports user interactions up.
/// </summary>
public interface IPanelHost
{
    /// <summary>Applies a panel definition (on activate / reconnect).</summary>
    void ApplyDefinition(string instanceId, UiPanel definition);

    /// <summary>Applies a filtered panel state change (DataStore.Changed derived).</summary>
    void ApplyStateChanged(PanelStateChange change);

    /// <summary>Raised when the user interacts with a control (upstream).</summary>
    event EventHandler<ControlInteractedEventArgs>? ControlInteracted;
}
