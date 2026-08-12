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
