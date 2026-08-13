using KitX.ToolKit.Models;

namespace KitX.ToolKit.Panels;

/// <summary>
/// Panel key derivation + main-property resolution (ToolKit 前后端分离 GUI 稿 §5.4).
/// Panel state lives in the DataStore under <c>{toolkitId}/{instanceId}/panel/{controlId}/{prop}</c>,
/// so the panel is a pure projection of the data blackboard and the instance's namespace is
/// shared by every run within that instance (including UIEvent-triggered chains).
/// </summary>
public static class PanelScope
{
    /// <summary>Builds a panel key: <c>{toolkitId}/{instanceId}/panel/{controlId}/{prop}</c>.</summary>
    public static string Key(string toolkitId, string instanceId, string controlId, string prop)
        => $"{toolkitId}/{instanceId}/panel/{controlId}/{prop}";

    /// <summary>
    /// The main-property key for a control type (the <c>UiSet(controlId, value)</c> default
    /// landing key). <c>Log</c>/<c>Dialog</c> return null — they are not <c>UiSet</c>-able
    /// (use <c>UiLog</c>/<c>UiDialog</c> instead).
    /// </summary>
    public static string? MainProperty(string type) => type switch
    {
        "Text" => "text",
        "Input" or "Number" or "Select" or "Switch" or "Progress" => "value",
        "Button" => "enabled",
        _ => null, // Icon / Log / Dialog
    };

    /// <summary>Resolves a control's main-property key, or null when the control is unknown or has no main property.</summary>
    public static string? MainPropertyKey(string toolkitId, string instanceId, UiControl control)
    {
        var prop = MainProperty(control.Type);
        return prop is null ? null : Key(toolkitId, instanceId, control.Id, prop);
    }
}
