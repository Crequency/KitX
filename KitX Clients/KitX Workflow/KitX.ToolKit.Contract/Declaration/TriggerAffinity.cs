namespace KitX.ToolKit.Contract.Declaration;

/// <summary>
/// Trigger <b>affinity</b> — a type-inbuilt classification (not user config) that decides
/// how a fired trigger relates to instances (ToolKit 实例模型定稿 D1).
/// </summary>
/// <remarks>
/// This lives beside <see cref="TriggerType"/> because it is a total function of the
/// discriminant, not behaviour: it is part of what a declaration <i>means</i>, which is why
/// ADR-0012 D3 keeps it in the contract rather than the implementation.
/// </remarks>
public static class TriggerAffinity
{
    /// <summary>
    /// True when firing this trigger type spawns a new instance; the payload becomes the
    /// instance's initial context. Manual / PluginEvent / Timer.
    /// </summary>
    public static bool IsSpawn(this TriggerType type)
        => type is TriggerType.Manual or TriggerType.PluginEvent or TriggerType.Timer;

    /// <summary>
    /// True when this trigger type fires only within an existing instance and never creates
    /// one. UIEvent (routes to the owning instance's panel) / WorkflowCompletion (an
    /// in-instance graph edge driven by the scheduler).
    /// </summary>
    public static bool IsIntra(this TriggerType type)
        => type is TriggerType.UIEvent or TriggerType.WorkflowCompletion;
}
