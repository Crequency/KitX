using System.Text.Json;

namespace KitX.ToolKit.Contract;

/// <summary>
/// Panel runtime (backend side): binding resolution, write-back gating, dialog slot
/// management, control-event routing (ToolKit 前后端分离 GUI 稿 §4.1). One runtime serves
/// all instances; every method is instance-scoped.
/// </summary>
/// <remarks>
/// <b>Removed at S5.5.</b> ADR-0013 D4 archives the panel host out of the Dashboard and
/// deletes this interface with it — a panel is not a first-class ToolKit concept
/// (ADR-0012 D3). It moves into the contract assembly at S0 only because the Dashboard
/// injects and implements it in nine places, so it has to be visible to a project that may
/// only reference the contract. The panel host archive keeps working because it also
/// references the contract.
/// </remarks>
public interface IPanelRuntime
{
    /// <summary>Reads a control's main-property value. Null when the control is unknown.</summary>
    JsonElement? GetControlValue(string instanceId, string controlId);

    /// <summary>
    /// Reads a Log control's accumulated entries, oldest first, in the same string form the
    /// live projection delivers. Empty when the control is unknown or has written nothing —
    /// used to hydrate a rebuilt panel's history.
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
