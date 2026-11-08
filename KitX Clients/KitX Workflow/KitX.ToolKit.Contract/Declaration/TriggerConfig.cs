namespace KitX.ToolKit.Contract.Declaration;

/// <summary>
/// Per-type trigger configuration. Flat nullable fields rather than polymorphic
/// subtypes — keeps the config a simple, Agent-friendly JSON document and avoids
/// polymorphic <c>$type</c> markers (consistent with the Bench RFC's "config is the
/// truth, no DSL parser" stance). Only the fields relevant to the trigger's
/// <see cref="Trigger.Type"/> are populated.
/// </summary>
public sealed class TriggerConfig
{
    // ── PluginEvent ──
    /// <summary>The plugin that fires the event.</summary>
    public string? PluginName { get; set; }

    /// <summary>Trigger name; null = any trigger of the plugin.</summary>
    public string? TriggerName { get; set; }

    // ── WorkflowCompletion ("canvas edge") ──
    /// <summary>Predecessor workflow id whose completion starts the target(s).</summary>
    public string? From { get; set; }

    // ── Timer ──
    /// <summary>
    /// Cron expression (5-field). Not yet supported (C7): a non-empty Cron is rejected by
    /// <c>ConfigValidator</c> at save/mount time — use
    /// <see cref="DueTimeMs"/>/<see cref="IntervalMs"/>/<see cref="OneShot"/> instead.
    /// </summary>
    public string? Cron { get; set; }

    /// <summary>Periodic interval in milliseconds (alternative to Cron).</summary>
    public double? IntervalMs { get; set; }

    /// <summary>Fire once then stop (e.g. "run at KitX launch"). Default false = periodic.</summary>
    public bool? OneShot { get; set; }

    /// <summary>Initial delay before the first fire, in milliseconds. Default 0.</summary>
    public double? DueTimeMs { get; set; }

    // ── UIEvent ──
    // Panel and Control were removed (Q79/Q82): the panel host is gone, so there is no control
    // to scope an event to. What is left is the name an external caller fires — the View sends
    // `command{event}`, the workflow reads its payload. Surfacing and layout are Surface's job
    // now, not the trigger's.
    /// <summary>Event name, e.g. <c>Click</c>, <c>Submit</c>, <c>Toggled</c>.</summary>
    public string? Event { get; set; }

    /// <summary>
    /// The names this event carries into its workflows, or null when the trigger declares none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Projection, not filtering. With a list, only the names on it reach the packet — a client
    /// sending an extra property does not get to create a workflow constant, which is what used to
    /// happen on the trigger path when every top-level scalar was flattened in.
    /// </para>
    /// <para>
    /// Null (absent) and an empty list mean the same thing here: nothing is projected. Empty rather
    /// than null would be the tidier declaration, but absent has to be legal too, because every
    /// existing <c>toolkit.json</c> was written before this field existed.
    /// </para>
    /// <para>
    /// Only meaningful for <see cref="TriggerType.UIEvent"/>. A timer or a completion edge has no
    /// client supplying values, so a non-empty list on those is a declaration mistake rather than
    /// a feature.
    /// </para>
    /// </remarks>
    public IReadOnlyList<TriggerField>? Fields { get; set; }
}
