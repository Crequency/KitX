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
    /// <summary>
    /// Stable storage id (GUID, assigned by <c>Storage.ToolkitStore</c> on create). The
    /// display name lives in <see cref="Meta.Name"/>. Empty for in-memory/sample configs —
    /// <see cref="GetId"/> falls back to the name.
    /// </summary>
    public string Id { get; set; } = string.Empty;

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

    /// <summary>
    /// Canvas comment nodes (ToolKit Bench UX v2 §4.2). Text is config truth; canvas
    /// position is intentionally not persisted.
    /// </summary>
    public List<ToolkitComment> Comments { get; set; } = [];

    /// <summary>
    /// Maximum number of concurrently-running instances this ToolKit may have. Null
    /// (default) = unlimited. When exceeded, a spawn is rejected and surfaced via an
    /// event (ToolKit 实例模型定稿 D7).
    /// </summary>
    public int? MaxInstances { get; set; }

    /// <summary>The canonical id: <see cref="Id"/> when set, else <see cref="ToolkitMeta.Name"/>.</summary>
    public string GetId() => string.IsNullOrWhiteSpace(Id) ? Meta.Name : Id;
}
