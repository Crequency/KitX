using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Data;

namespace KitX.ToolKit.Compile;

/// <summary>
/// Everything the runtime needs about a trigger, resolved at compile time (ADR-0002 D1: "the
/// runtime MUST NOT take a <c>Toolkit</c> again").
/// </summary>
/// <remarks>
/// <para><b>Why the original <see cref="Trigger"/> is carried here.</b> The built-in trigger
/// sources are constructed from a <see cref="Trigger"/> — <c>TimerTrigger</c> reads
/// <c>Config.DueTimeMs</c>/<c>IntervalMs</c>/<c>OneShot</c>, <c>PluginEventTrigger</c> reads
/// <c>Config.PluginName</c>/<c>TriggerName</c>. Keeping the declaration on the compiled trigger
/// is what lets the manager build sources without holding the whole config: it carries the one
/// trigger it needs, not the document it came from.</para>
///
/// <para>This is deliberately not a blanket permission to re-read config. A compiled trigger is
/// a snapshot of a trigger the compiler has already validated; anything derivable (targets,
/// bindings, spawn-ness, affinity) is materialised as its own property so callers use the
/// resolved form rather than re-reading raw fields.</para>
/// </remarks>
internal abstract record CompiledTrigger(string Id, TriggerType Type)
{
    /// <summary>Workflow ids this trigger starts when it fires.</summary>
    public IReadOnlyList<string> Targets { get; init; } = [];

    /// <summary>
    /// The bindings this trigger declares, materialised so the scheduler never reads them from
    /// the config (ADR-0002 D1: "the runtime must never take a Toolkit again").
    ///
    /// <para>Only trigger bindings appear here. Completion edges have none — an edge carries
    /// topology, and the node it starts receives the predecessor's whole output packet
    /// (design Q44).</para>
    /// </summary>
    public IReadOnlyList<TriggerBinding> Bindings { get; init; } = [];

    /// <summary>
    /// The validated declaration this was compiled from. Present so a trigger source can be
    /// constructed without the manager holding the config document; see this type's remarks.
    /// </summary>
    public required Trigger Declaration { get; init; }

    /// <summary>True when firing starts a new flow (Manual / PluginEvent / Timer).</summary>
    public bool Spawns => Type.IsSpawn();

    /// <summary>True when firing routes into an existing instance rather than spawning.</summary>
    public bool IsIntra => !Spawns;
}

/// <summary>A manual trigger: starts an instance on explicit request.</summary>
internal sealed record CompiledManualTrigger(string Id) : CompiledTrigger(Id, TriggerType.Manual);

/// <summary>A plugin-event trigger: starts an instance when the named plugin event fires.</summary>
internal sealed record CompiledPluginEventTrigger(
    string Id,
    string PluginName,
    string? TriggerName) : CompiledTrigger(Id, TriggerType.PluginEvent);

/// <summary>A timer trigger: starts an instance on a fixed period or one-off delay.</summary>
internal sealed record CompiledTimerTrigger(
    string Id,
    double DueTimeMs,
    double? IntervalMs,
    bool OneShot) : CompiledTrigger(Id, TriggerType.Timer);

/// <summary>
/// A UI-event trigger: routes an external event into an existing instance. Named for the
/// trigger type it declares (<see cref="TriggerType.UIEvent"/>), not for a control — controls
/// were removed in Q79/Q82; the event name is the only thing left to match on.
/// </summary>
internal sealed record CompiledUiEventTrigger(
    string Id,
    string? Event) : CompiledTrigger(Id, TriggerType.UIEvent);

/// <summary>
/// A workflow-completion trigger — "canvas edge". It contributes edges to
/// <see cref="CompiledToolkit.Graph"/> rather than forming a trigger of its own; the
/// compiler keeps it here so callers can still enumerate every declaration.
/// </summary>
internal sealed record CompiledCompletionTrigger(
    string Id,
    string From) : CompiledTrigger(Id, TriggerType.WorkflowCompletion);
