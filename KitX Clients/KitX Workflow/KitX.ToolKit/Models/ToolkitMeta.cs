namespace KitX.ToolKit.Models;

/// <summary>
/// ToolKit metadata envelope (Bench RFC §7.2 <c>Meta</c>).
/// </summary>
public sealed class ToolkitMeta
{
    /// <summary>Display name of the ToolKit.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SemVer of the ToolKit.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Author identity (display name / handle).</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Optional emoji / icon path for the ToolKit card.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Short human-readable description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Minimum KitX version required to run this ToolKit (e.g. "3.25.4.0").</summary>
    public string MinKitXVersion { get; set; } = "0.0.0.0";

    /// <summary>Free-form discovery tags.</summary>
    public List<string> Tags { get; set; } = [];
}
