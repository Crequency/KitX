namespace KitX.ToolKit.Models;

/// <summary>
/// A reference to a workflow bundled inside a ToolKit (Bench RFC §7.2 <c>Workflows[]</c>).
/// The <see cref="File"/> path is relative to the ToolKit storage root; at runtime the
/// resolved <c>.kcs</c> is loaded through <see cref="KitX.Core.Contract.Workflow.IWorkflowStorageService"/>.
/// </summary>
public sealed class ToolkitWorkflow
{
    /// <summary>Stable logical id used across the config (bindings, edges reference this).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Path to the physical <c>.kcs</c> file (relative to the ToolKit storage root).</summary>
    public string File { get; set; } = string.Empty;
}
