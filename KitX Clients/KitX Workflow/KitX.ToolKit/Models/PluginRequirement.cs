namespace KitX.ToolKit.Models;

/// <summary>
/// A plugin requirement declaration (Bench RFC §7.2 <c>Plugins[]</c>). This is a
/// declaration, NOT a plugin entity — the ToolKit records what it needs and the
/// host resolves it against the unified plugin pool (local installed / networked).
/// </summary>
public sealed class PluginRequirement
{
    /// <summary>Plugin display name, e.g. <c>KitX.AI.Plugin</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Version constraint, e.g. <c>&gt;=1.0.0</c>.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Source hint: <c>local</c> (installed on this device) or a networked/device source.</summary>
    public string Source { get; set; } = "local";
}
