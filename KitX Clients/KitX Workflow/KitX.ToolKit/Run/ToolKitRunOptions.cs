namespace KitX.ToolKit.Run;

/// <summary>
/// Tunables for running a ToolKit's workflow graph.
/// </summary>
/// <remarks>
/// <para>Two limits exist and they are not interchangeable. <see cref="MaxConcurrentNodes"/>
/// bounds how many node bodies of one run execute at once (ADR-0008 D2). Flow-level admission
/// — how many runs may exist at once — belongs to the scheduler and is separate on purpose:
/// a single wide run and many narrow runs are different pressures, and one number cannot
/// express both.</para>
/// </remarks>
public sealed class ToolKitRunOptions
{
    /// <summary>
    /// Maximum number of node bodies a single run may execute concurrently. Zero or negative
    /// resolves to the machine's processor count.
    /// </summary>
    /// <remarks>
    /// Raising this helps only when the workflows are I/O-bound; the script compiler that backs
    /// execution is not thread-safe (ADR-0008 D3, tracked outside this assembly), so a very
    /// large value trades throughput for contention.
    /// </remarks>
    public int MaxConcurrentNodes { get; set; }
}
