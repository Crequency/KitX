namespace KitX.ToolKit.Instances;

/// <summary>
/// Tunables for <see cref="ToolkitInstanceManager"/> retention and cleanup. A single
/// defaults-constructed instance is used when no options are supplied to the manager.
/// </summary>
public sealed class ToolkitInstanceManagerOptions
{
    /// <summary>
    /// Maximum number of <see cref="InstanceStatus.Completed"/> instances the manager
    /// retains before the oldest (by <c>CompletedAt</c>) are ended and evicted. Zero or
    /// negative means unlimited — no eviction ever runs.
    /// </summary>
    public int CompletedInstanceCap { get; set; } = 200;
}
