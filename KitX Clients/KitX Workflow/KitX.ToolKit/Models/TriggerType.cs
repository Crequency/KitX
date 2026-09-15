namespace KitX.ToolKit.Models;

/// <summary>
/// The unified set of trigger sources a <see cref="Toolkit"/> can declare.
/// Every trigger source collapses onto this single discriminant; the
/// per-type specifics live in <see cref="TriggerConfig"/>. This mirrors the
/// Bench RFC §4.2 (Manual / PluginEvent / UIEvent / WorkflowCompletion / Timer).
/// </summary>
public enum TriggerType
{
    /// <summary>Manual run from the canvas / panel / CLI. Never auto-fires.</summary>
    Manual,

    /// <summary>A plugin event (<c>PluginName</c> + <c>TriggerName</c>), routed by the
    /// existing plugin-event infrastructure.</summary>
    PluginEvent,

    /// <summary>A UI-panel control event (button click / input submit / switch toggle).
    /// Declared now; the runtime source is a skeleton until the GUI framework lands.</summary>
    UIEvent,

    /// <summary>Another workflow completing (the "canvas edge" in harness terms). Driven by
    /// the Bench scheduler, not by an external event pump.</summary>
    WorkflowCompletion,

    /// <summary>A timer — one-shot or periodic (Cron). "Run at KitX launch" is a
    /// one-shot special case of this type.</summary>
    Timer,
}

/// <summary>
/// Trigger <b>affinity</b> — a type-inbuilt classification (not user config) that
/// decides how a fired trigger relates to instances (ToolKit 实例模型定稿 D1).
/// <list type="bullet">
///   <item><b>Spawn</b> — firing <i>creates a new instance</i>; the payload becomes the
///   instance's initial context. Manual / PluginEvent / Timer.</item>
///   <item><b>Intra</b> — fires <i>within an existing instance</i>; it never creates one.
///   UIEvent (routes to the owning instance's panel) / WorkflowCompletion (an in-instance
///   graph edge driven by the scheduler).</item>
/// </list>
/// </summary>
public static class TriggerAffinity
{
    /// <summary>True when firing this trigger type spawns a new instance.</summary>
    public static bool IsSpawn(this TriggerType type)
        => type is TriggerType.Manual or TriggerType.PluginEvent or TriggerType.Timer;

    /// <summary>True when this trigger type fires within an existing instance only.</summary>
    public static bool IsIntra(this TriggerType type)
        => type is TriggerType.UIEvent or TriggerType.WorkflowCompletion;
}
