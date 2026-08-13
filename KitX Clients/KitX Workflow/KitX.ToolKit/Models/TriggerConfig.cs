namespace KitX.ToolKit.Models;

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
    /// <summary>Cron expression (5-field). When set, takes precedence over <see cref="IntervalMs"/>.</summary>
    public string? Cron { get; set; }

    /// <summary>Periodic interval in milliseconds (alternative to Cron).</summary>
    public double? IntervalMs { get; set; }

    /// <summary>Fire once then stop (e.g. "run at KitX launch"). Default false = periodic.</summary>
    public bool? OneShot { get; set; }

    /// <summary>Initial delay before the first fire, in milliseconds. Default 0.</summary>
    public double? DueTimeMs { get; set; }

    // ── UIEvent (declared; runtime skeleton until the GUI iteration) ──
    /// <summary>UI panel id the control belongs to.</summary>
    public string? Panel { get; set; }

    /// <summary>Control id that raises the event.</summary>
    public string? Control { get; set; }

    /// <summary>Event name, e.g. <c>Click</c>, <c>Submit</c>, <c>Toggled</c>.</summary>
    public string? Event { get; set; }

    // ── Spawn presentation (Manual / PluginEvent / Timer) ──
    /// <summary>
    /// How a spawned instance's panel is presented to the user. <c>auto</c> (default) =
    /// automatically open/focus the instance panel on spawn; <c>silent</c> = run in the
    /// background (a badge hints it) and only surface when the workflow calls
    /// <c>KitX.UI.OpenPanel</c> or the user opens it manually.
    /// </summary>
    public string? Surface { get; set; }
}
