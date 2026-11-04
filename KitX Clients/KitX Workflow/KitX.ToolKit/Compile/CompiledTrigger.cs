using KitX.ToolKit.Contract.Declaration;

namespace KitX.ToolKit.Compile;

/// <summary>
/// A trigger declaration resolved into its closed form (ADR-0002 D1).
///
/// <para>The config represents a trigger as one <see cref="TriggerType"/> plus a bag of
/// nullable <see cref="TriggerConfig"/> fields — 11 properties of which at most a few are
/// meaningful for any given type. That shape is good for authoring (an Agent writes a flat
/// JSON document) and bad for execution (every consumer re-checks "which fields matter
/// here?"). This type is the closed form: the discriminant is fixed and only the settings
/// that actually apply are materialised.</para>
/// </summary>
public abstract record CompiledTrigger(string Id, TriggerType Type)
{
    /// <summary>Workflow ids this trigger starts when it fires.</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    /// <summary>True when firing starts a new instance (Manual / PluginEvent / Timer).</summary>
    public bool Spawns => Type.IsSpawn();
}

/// <summary>A manual trigger: starts an instance on explicit request.</summary>
public sealed record CompiledManualTrigger(string Id) : CompiledTrigger(Id, TriggerType.Manual);

/// <summary>A plugin-event trigger: starts an instance when the named plugin event fires.</summary>
public sealed record CompiledPluginEventTrigger(
    string Id,
    string PluginName,
    string? TriggerName) : CompiledTrigger(Id, TriggerType.PluginEvent);

/// <summary>A timer trigger: starts an instance on a fixed period or one-off delay.</summary>
public sealed record CompiledTimerTrigger(
    string Id,
    double DueTimeMs,
    double? IntervalMs,
    bool OneShot) : CompiledTrigger(Id, TriggerType.Timer);

/// <summary>A UI-event trigger: routes a panel control event into an existing instance.</summary>
public sealed record CompiledUiEventTrigger(
    string Id,
    string? Control,
    string? Event) : CompiledTrigger(Id, TriggerType.UIEvent);

/// <summary>
/// A workflow-completion trigger — "canvas edge". It contributes edges to
/// <see cref="CompiledToolkit.Graph"/> rather than forming a trigger of its own; the
/// compiler keeps it here so callers can still enumerate every declaration.
/// </summary>
public sealed record CompiledCompletionTrigger(
    string Id,
    string From) : CompiledTrigger(Id, TriggerType.WorkflowCompletion);
