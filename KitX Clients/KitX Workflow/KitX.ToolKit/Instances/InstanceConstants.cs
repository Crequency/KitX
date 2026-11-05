namespace KitX.ToolKit.Instances;

/// <summary>
/// Reserved constant injected into every workflow of a spawned instance, carrying the
/// instance's id. The name is defined in WorkflowV6 (<see cref="KitX.WorkflowV6.ToolKitConstants.InstanceId"/>)
/// and injected per-run into <c>ExecutionGlobals.InstanceId</c> by the execution backend, so
/// workflow authors no longer declare or pass it manually.
/// </summary>
internal static class InstanceConstants
{
    /// <summary>Reserved constant carrying the instance id.</summary>
    public const string InstanceId = KitX.WorkflowV6.ToolKitConstants.InstanceId;

    /// <summary>
    /// The spawn trigger's <c>Surface</c> value meaning "run in the background". Centralized so
    /// the magic string is defined once; the field it fills
    /// (<see cref="Contract.InstanceSnapshot.Surface"/>) has no production consumer today.
    /// </summary>
    public const string SurfaceSilent = "silent";
}
