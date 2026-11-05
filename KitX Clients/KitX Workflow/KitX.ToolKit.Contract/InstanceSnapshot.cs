namespace KitX.ToolKit.Contract;

/// <summary>
/// A read-only snapshot of a running or completed instance, for the run monitor and the
/// future remote directory (ToolKit 实例模型定稿 D4/D6). Immutable and serializable so it
/// can cross the contract boundary.
/// </summary>
/// <remarks>
/// <para><b>Two derived members were dropped at S0.</b> The pre-S0 version exposed
/// <c>IsSilent</c> and <c>RunSummary</c>, both of which read
/// <c>Instances.InstanceConstants</c> — a concrete type in the implementation assembly.
/// That is a contract-to-implementation reference, and no amount of doc-comment rewriting
/// fixes it: they were real member accesses, not <c>&lt;see cref&gt;</c> links. Removing
/// them is what lets this record live in the contract assembly at all.</para>
/// <para>If a presentation-level summary is still wanted, it belongs in the View layer
/// (which may format numbers however it likes), not in the contract.</para>
/// </remarks>
public sealed record InstanceSnapshot(
    string InstanceId,
    string ToolkitId,
    string TriggerId,
    Initiator Initiator,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int ActiveRuns,
    int CompletedRuns,
    int FailedRuns)
{
    /// <summary>
    /// How the instance's current activity window ended, or <see langword="null"/> while it is
    /// still running.
    /// </summary>
    /// <remarks>
    /// <para><b>Replaces <c>InstanceStatus</c>, removed at S4 (ADR-0005 D1).</b> The old enum
    /// conflated two axes: whether the work has stopped (a moment) and how it went (a verdict).
    /// "Has it stopped" is fully expressed by this property being null, so the enum carried no
    /// information the outcome does not already carry — and it made a cancelled run and a failed
    /// run indistinguishable, which is the defect ADR-0005 exists to remove.</para>
    /// <para>The reason travels with the verdict rather than being looked up separately, because
    /// a <see cref="RunOutcome.Failed"/> that cannot say why is not actionable (ADR-0005 D4).</para>
    /// </remarks>
    public RunOutcome? Outcome { get; init; }

    /// <summary>
    /// Why the window ended the way it did. Present when <see cref="Outcome"/> is
    /// <see cref="RunOutcome.Failed"/> (ADR-0005 D4); <see cref="Diagnostic.Code"/> is the stable,
    /// branchable part and <see cref="Diagnostic.Message"/> is for humans.
    /// </summary>
    public Diagnostic? Failure { get; init; }

    /// <summary>True while the instance still has work in flight.</summary>
    public bool IsRunning => Outcome is null;

    /// <summary>True once the instance has stopped and its window succeeded.</summary>
    public bool Succeeded => Outcome == RunOutcome.Succeeded;

    /// <summary>
    /// The spawn trigger's presentation mode (<c>auto</c>/<c>silent</c>). Null when unknown.
    /// </summary>
    /// <remarks>
    /// Filled today from <c>TriggerConfig.Surface</c>. <b>No production consumer</b>: the panel
    /// host that read it was archived at S5.5, and the Bench window does not show it. The field
    /// stays because where "how to present this instance" belongs is a View-layer question that
    /// has not been decided yet — deleting it now would decide the answer by accident. Its fate
    /// is settled together with the View contract, not here.
    /// </remarks>
    public string? Surface { get; init; }
}
