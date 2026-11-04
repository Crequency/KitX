namespace KitX.ToolKit.Contract.Declaration;

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
    /// Stable storage id (a GUID, assigned on create). The display name lives in
    /// <see cref="ToolkitMeta.Name"/>. Empty for in-memory / sample configs.
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

    /// <summary>
    /// The optional UI panel definition.
    /// </summary>
    /// <remarks>
    /// <para><b>Deprecated at S0; removed at S5.5.</b> ADR-0012 D3 and ADR-0013 D4 delete the panel
    /// host from ToolKit — a panel is not a first-class ToolKit concept. It stays on the contract
    /// for now because <c>UiPanel</c> is reachable through this property, so removing it here at S0
    /// would force Dashboard code changes beyond <c>using</c> (the S0 completion criterion).</para>
    /// <para>Removal order: S5.5 archives the panel host, then this property and the
    /// <c>UiPanel</c> / <c>UiControl</c> types go together.</para>
    /// </remarks>
    public UiPanel? UiPanel { get; set; }

    /// <summary>
    /// Canvas comment nodes (ToolKit Bench UX v2 §4.2). Text is config truth; canvas
    /// position is intentionally not persisted.
    /// </summary>
    public List<ToolkitComment> Comments { get; set; } = [];

    /// <summary>
    /// The ToolKit's key table (ADR-0004 D2/D3, design §5.1).
    /// </summary>
    /// <remarks>
    /// <para>Workflows write key names; the system turns them into fully qualified keys. Declaring
    /// them here is what makes the data plane a table instead of an open string space, and it is
    /// the only remaining source of regions.</para>
    ///
    /// <para>Empty is legal — a ToolKit that keeps no state declares no keys. A workflow writing a
    /// key that is not declared here is a load-time error.</para>
    /// </remarks>
    public List<KeyDeclaration> Keys { get; set; } = [];

    /// <summary>
    /// Maximum number of concurrently-running instances this ToolKit may have. Null
    /// (default) = unlimited. When exceeded, a spawn is rejected and surfaced via an
    /// event (ToolKit 实例模型定稿 D7).
    /// </summary>
    public int? MaxInstances { get; set; }

    /// <summary>The canonical id: <see cref="Id"/> when set, else <see cref="ToolkitMeta.Name"/>.</summary>
    public string GetId() => string.IsNullOrWhiteSpace(Id) ? Meta.Name : Id;
}
