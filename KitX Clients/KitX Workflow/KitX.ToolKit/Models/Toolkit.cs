namespace KitX.ToolKit.Models;

/// <summary>
/// The ToolKit config — the single source of truth for a ToolKit (Bench RFC §7).
///
/// This is a pure data model, deliberately UI-agnostic: it is the format an AI
/// Agent writes directly (schema-constrained JSON), the Bench canvas edits
/// (canvas = projection), and the mermaid export reads. It carries no behaviour.
/// </summary>
public sealed class Toolkit
{
    /// <summary>ToolKit metadata (name / version / author / icon / description / MinKitXVersion / tags).</summary>
    public ToolkitMeta Meta { get; set; } = new();

    /// <summary>The workflows bundled inside this ToolKit (physical <c>.kcs</c> files).</summary>
    public List<ToolkitWorkflow> Workflows { get; set; } = [];

    /// <summary>Plugin requirement declarations (name + version + source) — not entities.</summary>
    public List<PluginRequirement> Plugins { get; set; } = [];

    /// <summary>All trigger relationships (including the "canvas edges").</summary>
    public List<Trigger> Triggers { get; set; } = [];

    /// <summary>Optional UI panel definition (declared now; rendering deferred to the GUI iteration).</summary>
    public UiPanel? UiPanel { get; set; }
}
