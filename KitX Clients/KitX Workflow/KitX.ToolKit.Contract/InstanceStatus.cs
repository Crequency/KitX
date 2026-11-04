namespace KitX.ToolKit.Contract;

/// <summary>
/// Lifecycle state of an instance as observed through <see cref="InstanceSnapshot"/>.
/// </summary>
/// <remarks>
/// <para><b>Transitional type — removed at S4.</b> It is the pre-rewrite enum that
/// conflates two different axes: lifecycle (durable) and execution outcome (one-shot).
/// <see cref="LifecycleState"/> and <see cref="RunOutcome"/> already exist in this assembly
/// and are the replacement.</para>
/// <para>It stays until S4 because <see cref="InstanceSnapshot.Status"/> is read by
/// Dashboard code (<c>snapshot.Status == InstanceStatus.Running</c>, and a
/// <c>with { Status = ... }</c> expression) and by an XAML value converter. Swapping the
/// property's type is a code change, not a <c>using</c> change, so it cannot happen in the
/// same step whose whole criterion is "only <c>using</c> changes".</para>
/// </remarks>
public enum InstanceStatus
{
    /// <summary>At least one workflow chain is running.</summary>
    Running,

    /// <summary>All chains finished; retained until explicitly ended.</summary>
    Completed,
}
