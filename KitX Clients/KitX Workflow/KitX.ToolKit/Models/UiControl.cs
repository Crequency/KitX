namespace KitX.ToolKit.Models;

/// <summary>
/// A single control in the fixed UI component set (Bench RFC §8.2). The component
/// type is a string discriminant (<c>Text</c>/<c>Icon</c>/<c>Button</c>/<c>Input</c>/
/// <c>Number</c>/<c>Select</c>/<c>Switch</c>/<c>Log</c>/<c>Progress</c>/<c>Dialog</c>);
/// <see cref="Options"/> carries type-specific props as a JSON object.
/// </summary>
public sealed class UiControl
{
    /// <summary>The fixed component type discriminant.</summary>
    public string Type { get; set; } = "Text";

    /// <summary>Control id — referenced by <c>UiGet</c>/<c>UiSet</c> and <c>UIEvent</c> triggers.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Initial / static text (Text, Button, Input placeholder...).</summary>
    public string? Text { get; set; }

    /// <summary>Type-specific props (e.g. <c>{"Items":[...]}</c> for Select) as a JSON object.</summary>
    public Dictionary<string, object?>? Options { get; set; }
}
