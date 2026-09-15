namespace KitX.ToolKit.Models;

/// <summary>
/// A canvas comment node (ToolKit Bench UX v2 §4.2 / C17). The comment <b>text</b> is part
/// of the config truth and persisted in <c>toolkit.json</c>; its canvas position is
/// intentionally not persisted (consistent with the "node locations are not config" rule).
/// </summary>
public sealed class ToolkitComment
{
    /// <summary>Stable logical id (unique within <see cref="Toolkit.Comments"/>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The visible annotation text.</summary>
    public string Text { get; set; } = string.Empty;
}
