namespace KitX.ToolKit.Models;

/// <summary>
/// A binding from a trigger to a target workflow (Bench RFC §4.1 / §7.2).
/// <c>Params</c> maps a target workflow's constant name to an injection source:
/// a <c>$payload.x</c> reference (trigger payload), a <c>$output.key</c> reference
/// (predecessor workflow data packet), or a literal value. Resolved at fire time into
/// constant overrides via <c>WorkflowOverrides.ApplyConstantOverrides</c>.
/// </summary>
public sealed class TriggerBinding
{
    /// <summary>The target workflow id (must exist in <see cref="Toolkit.Workflows"/>).</summary>
    public string Workflow { get; set; } = string.Empty;

    /// <summary>Constant-name → injection source mapping. Empty = no parameter injection.</summary>
    public Dictionary<string, string?> Params { get; set; } = [];
}
