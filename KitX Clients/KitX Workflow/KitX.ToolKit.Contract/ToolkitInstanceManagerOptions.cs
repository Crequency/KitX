namespace KitX.ToolKit.Contract;

/// <summary>
/// Tunables for instance retention and cleanup. The host registers an instance of this after
/// <c>AddKitXToolKit</c> to override the default (last registration wins).
/// </summary>
/// <remarks>
/// In the contract because a host has to configure it. It carries no implementation detail —
/// it is a single integer — so keeping it private would have forced the host to name the
/// implementation assembly just to say how many finished instances to keep.
/// </remarks>
public sealed class ToolkitInstanceManagerOptions
{
    /// <summary>
    /// Maximum number of completed instances retained before the oldest (by completion time)
    /// are ended and evicted. Zero or negative means unlimited — no eviction ever runs.
    /// </summary>
    public int CompletedInstanceCap { get; set; } = 200;
}
