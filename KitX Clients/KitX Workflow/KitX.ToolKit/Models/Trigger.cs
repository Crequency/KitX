namespace KitX.ToolKit.Models;

/// <summary>
/// A single trigger source declaration (Bench RFC §4.2 / §7.2 <c>Triggers[]</c>).
/// Encapsulates every trigger relationship a ToolKit declares, including the
/// "canvas edges" (<see cref="TriggerType.WorkflowCompletion"/>).
/// </summary>
public sealed class Trigger
{
    /// <summary>Stable logical id (referenced nowhere else — used for scoping/logging).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The unified trigger type discriminant.</summary>
    public TriggerType Type { get; set; }

    /// <summary>Per-type configuration (only the fields relevant to <see cref="Type"/> are populated).</summary>
    public TriggerConfig Config { get; set; } = new();

    /// <summary>The workflows to start (and parameters to inject) when this trigger fires.</summary>
    public List<TriggerBinding> Bindings { get; set; } = [];
}
