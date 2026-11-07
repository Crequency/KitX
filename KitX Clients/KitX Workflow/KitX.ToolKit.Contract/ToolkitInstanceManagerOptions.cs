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

    /// <summary>
    /// Machine-wide cap on concurrently-running flows, summed across every mounted ToolKit.
    /// A trigger that would exceed it is rejected with code <c>concurrency-limit</c>.
    /// Zero or negative means unlimited.
    /// </summary>
    /// <remarks>
    /// <para><b>Why machine-wide and not per-ToolKit.</b> The old <c>Toolkit.MaxInstances</c> was
    /// a per-ToolKit field. Moving the cap to a Core setting changes its scope: three chatty
    /// ToolKits at 256 each would be 768 concurrent flows, which does not protect the machine —
    /// and protecting the machine is the only reason the knob exists. The cost is real and
    /// accepted: one busy ToolKit can consume the budget and other ToolKits get rejected until
    /// someone raises the number. That is observable and fixed by turning one dial.</para>
    ///
    /// <para><b>Why flows and not instances.</b> One instance can carry several runs — the Spawn
    /// run plus every UIEvent chain started later (<c>ToolkitInstance.TrackRun</c>). Counting
    /// instances would let a busy ToolKit exceed the cap through a path that creates no instance
    /// but is still concurrent work. The count comes from
    /// <c>RunScheduler.ActiveInstanceCount</c> (<c>_runs.Count</c>), which increments and
    /// decrements exactly with run lifetime.</para>
    ///
    /// <para><b>Default 256</b> is a safety rail against a runaway source, not a tuning knob.
    /// Editable in Dashboard → Performance; read once at startup, following the same
    /// "Applies at startup" convention as the other performance settings.</para>
    /// </remarks>
    public int MaxConcurrentFlows { get; set; } = 256;
}
