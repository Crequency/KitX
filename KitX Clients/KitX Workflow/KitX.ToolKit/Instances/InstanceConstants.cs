namespace KitX.ToolKit.Instances;

/// <summary>
/// Reserved constant injected into every workflow of a spawned flow, carrying the flow's id. The
/// name is defined in WorkflowV6 (<see cref="KitX.WorkflowV6.ToolKitConstants.InstanceId"/>) and
/// injected per-run into <c>ExecutionGlobals.InstanceId</c> by the execution backend, so workflow
/// authors no longer declare or pass it manually.
/// </summary>
/// <remarks>
/// <c>SurfaceSilent</c> used to live here as well, spelling the spawn trigger's
/// <c>Surface</c> value. Both the field it filled (<c>InstanceSnapshot.Surface</c>) and the config
/// member it read (<c>TriggerConfig.Surface</c>) were removed on 2026-11-08 — how a run should be
/// presented is a View-layer question, and the View contract has not been written yet. The
/// constant had no remaining consumer, so it went with them rather than sitting here as a string
/// nobody reads.
/// </remarks>
internal static class InstanceConstants
{
    /// <summary>Reserved constant carrying the flow id.</summary>
    public const string InstanceId = KitX.WorkflowV6.ToolKitConstants.InstanceId;
}
