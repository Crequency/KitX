using System.Text.Json;
using KitX.ToolKit.Models;

namespace KitX.ToolKit.Contracts;

/// <summary>
/// Panel runtime (backend side): binding resolution, write-back gating, dialog slot
/// management, control-event routing (ToolKit 前后端分离 GUI 稿 §4.1). One runtime serves
/// all instances; every method is instance-scoped.
/// </summary>
public interface IPanelRuntime
{
    /// <summary>Reads a control's main-property value (the <c>UiGet</c> backend). Null when the control is unknown.</summary>
    JsonElement? GetControlValue(string instanceId, string controlId);

    /// <summary>
    /// Reads a Log control's accumulated entries (the <c>UiLog</c> landing key), oldest
    /// first, in the same string form the live projection delivers. Empty when the control
    /// is unknown or has written nothing — used to hydrate a rebuilt panel's history.
    /// </summary>
    IReadOnlyList<string> GetControlLog(string instanceId, string controlId);

    /// <summary>
    /// Frontend write-back: sets a control's main-property value. Gated — only the control's
    /// own bound key may be written; unknown controls are rejected.
    /// </summary>
    void SetControlValue(string instanceId, string controlId, object? value);

    /// <summary>Routes a control event to the owning instance's UIEvent trigger chain.</summary>
    void RaiseControlEvent(string instanceId, string controlId, string eventName, object? value);

    /// <summary>Requests the host to present (open/focus) an instance's panel.</summary>
    void RequestPanelOpen(string instanceId);
}
