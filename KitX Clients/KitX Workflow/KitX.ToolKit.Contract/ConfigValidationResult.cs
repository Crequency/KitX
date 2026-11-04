namespace KitX.ToolKit.Contract;

/// <summary>
/// The diagnostics produced by <see cref="ConfigValidator"/>. Immutable: a result that can be
/// appended to after the fact is a result two callers can disagree about.
/// </summary>
/// <remarks>
/// Strings rather than <see cref="Diagnostic"/> on purpose. These messages are shown verbatim in
/// the editor's error list, and two of them are pattern-matched by text
/// (<c>Contains("cycle")</c>) to decide whether to roll back an edge. Migrating to structured
/// diagnostics would change how the editor behaves, which is not what the contract split is for.
/// </remarks>
public sealed class ConfigValidationResult
{
    /// <summary>Creates a result from the collected diagnostics.</summary>
    public ConfigValidationResult(IEnumerable<string>? errors = null)
        => Errors = errors is null ? [] : [.. errors];

    /// <summary>True when the config is structurally sound and its graph is a strict DAG.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Collected diagnostics. Empty when valid.</summary>
    public IReadOnlyList<string> Errors { get; }
}
