namespace KitX.ToolKit.Instances;

/// <summary>Why an instance was torn down.</summary>
/// <remarks>
/// <para>ADR-0005 asks that each termination carry a reason. The alternative — a boolean
/// "cancelled" flag — cannot distinguish an orderly shutdown from an eviction, and the two are
/// read very differently when someone is looking at a log after the fact.</para>
/// <para>This is <b>not</b> a lifecycle state and <b>not</b> a run outcome: the instance is gone
/// in every case here. It is the reason it went. That is why it lives apart from
/// <see cref="KitX.ToolKit.Contract.LifecycleState"/> (which describes a toolkit that still
/// exists) and <see cref="KitX.ToolKit.Contract.RunOutcome"/> (which describes a flow that
/// finished on its own).</para>
/// </remarks>
internal enum InstanceEndReason
{
    /// <summary>The process or the host is shutting its toolkits down in an orderly way.</summary>
    Shutdown,

    /// <summary>The owning toolkit was unmounted; its instances could not outlive it.</summary>
    Unmount,

    /// <summary>The owning toolkit was disabled. Unlike <see cref="Unmount"/> the toolkit and its
    /// declared keys survive; only the flows stop.</summary>
    Disabled,

    /// <summary>The owning toolkit was uninstalled; instances and data both go.</summary>
    Uninstall,

    /// <summary>An operator asked for this specific instance to stop.</summary>
    Requested,

    /// <summary>Evicted by <c>CompletedInstanceCap</c> to bound the retention of finished runs.</summary>
    Evicted,
}
