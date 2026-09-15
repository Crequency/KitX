namespace KitX.ToolKit.Validation;

/// <summary>
/// Result of <see cref="ConfigValidator.Validate"/>. Carries the collected
/// human-readable diagnostics so callers (host / Agent tooling / future canvas)
/// can surface exactly what is wrong.
/// </summary>
public sealed class ConfigValidationResult
{
    /// <summary>True when the config is structurally sound and its graph is a strict DAG.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Collected diagnostics. Empty when valid.</summary>
    public List<string> Errors { get; } = [];

    internal void Add(string message) => Errors.Add(message);
}
