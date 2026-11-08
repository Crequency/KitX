namespace KitX.ToolKit.Instances;

/// <summary>
/// Reserved constant names the ToolKit injects into every workflow run.
/// </summary>
/// <remarks>
/// <para>
/// These are flow-level facts, and they travel as constants rather than as packet properties.
/// That split is deliberate (design Q89): a packet is the event's payload — something a client
/// sends — while these say <em>whose run this is</em>. A client that could put a property called
/// <c>__KitXToolkitId__</c> into its payload would otherwise be able to choose which ToolKit's key
/// table its workflow writes into.
/// </para>
/// <para>
/// The names share one source of truth with WorkflowV6 (which injects them) so the two sides
/// cannot drift.
/// </para>
/// <para>
/// <c>SurfaceSilent</c> used to live here as well, spelling the spawn trigger's <c>Surface</c>
/// value. Both the field it filled (<c>InstanceSnapshot.Surface</c>) and the config member it read
/// (<c>TriggerConfig.Surface</c>) were removed on 2026-11-08 — how a run should be presented is a
/// View-layer question, and the View contract has not been written yet. The constant had no
/// remaining consumer, so it went with them rather than sitting here as a string nobody reads.
/// </para>
/// </remarks>
internal static class InstanceConstants
{
    /// <summary>
    /// The id of the ToolKit this run belongs to.
    /// </summary>
    /// <remarks>
    /// This is what scopes the <c>Toolkit*</c> builtins to a key table. It used to be the run's own
    /// id, resolved through the instance table on the way to the ToolKit — a hop that could fail
    /// for a run whose ToolKit was plainly still mounted.
    /// </remarks>
    public const string ToolkitId = KitX.WorkflowV6.ToolKitConstants.ToolkitId;

    /// <summary>
    /// The id of the trigger that started this run.
    /// </summary>
    /// <remarks>
    /// Flow-level for the same reason as <see cref="ToolkitId"/>: every node of one flow was
    /// started by the same trigger, and a client must not be able to claim otherwise by sending a
    /// property with this name.
    /// </remarks>
    public const string TriggerId = "__KitXTriggerId__";
}
