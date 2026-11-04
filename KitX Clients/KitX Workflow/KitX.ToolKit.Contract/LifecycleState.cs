namespace KitX.ToolKit.Contract;

/// <summary>
/// Lifecycle of a ToolKit installation (ADR-0005 D1). <b>Persisted</b> — a disabled
/// ToolKit is still disabled after a restart.
///
/// <para>This is deliberately a different type from <see cref="RunOutcome"/>: lifecycle
/// (durable, one per installed ToolKit) and execution outcome (one-shot, one per flow)
/// are two different axes. Collapsing them into a single enum is what produced four of
/// the original implementation gaps documented in ADR-0005.</para>
/// </summary>
public enum LifecycleState
{
    /// <summary>On disk, not yet enabled. Data present; triggers inactive.</summary>
    Installed,

    /// <summary>Triggers active, flows may start. Data present.</summary>
    Enabled,

    /// <summary>Triggers stopped and running flows cancelled, but <b>data is retained</b>.
    /// Connects and reads still work; only triggering is refused (error code
    /// <c>toolkit.not-enabled</c>). This is NOT the same as uninstall.</summary>
    Disabled,
}
