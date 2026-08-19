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
    /// <summary>
    /// The <c>/panel/</c> path segment that marks a DataStore key as an instance panel
    /// projection. Single source of truth shared by the build side
    /// (<see cref="Key"/>) and the parse side (<c>TryParsePanelKey</c> in
    /// <see cref="KitX.ToolKit.Instances.ToolkitInstanceManager"/>) so the two can never drift.
    /// </summary>
    public const string PanelKeySegment = "/panel/";

    /// <summary>Dialog request slot prop name (the <c>UiDialog</c> landing key).</summary>
    public const string PropRequest = "request";

    /// <summary>Log control prop name (the <c>UiLog</c> landing key).</summary>
    public const string PropLog = "log";

    /// <summary>The value/input/select/switch/progress main-property name.</summary>
    public const string PropValue = "value";

    /// <summary>The dialog confirm UI event name (host → backend, clears the request slot).</summary>
    public const string EventConfirm = "Confirm";

    /// <summary>Builds a panel key: <c>{toolkitId}/{instanceId}/panel/{controlId}/{prop}</c>.</summary>
    public static string Key(string toolkitId, string instanceId, string controlId, string prop)
        => $"{toolkitId}/{instanceId}{PanelKeySegment}{controlId}/{prop}";

    /// <summary>
    /// The main-property key for a control type (the <c>UiSet(controlId, value)</c> default
    /// landing key). <c>Log</c>/<c>Dialog</c> return null — they are not <c>UiSet</c>-able
    /// (use <c>UiLog</c>/<c>UiDialog</c> instead).
    /// </summary>
    public static string? MainProperty(string type) => type switch
    {
        "Text" => "text",
        "Input" or "Number" or "Select" or "Switch" or "Progress" => PropValue,
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
