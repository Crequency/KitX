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
    InstanceStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int ActiveRuns,
    int CompletedRuns,
    int FailedRuns)
{
    /// <summary>
    /// The spawn trigger's presentation mode (<c>auto</c>/<c>silent</c>). Null when unknown.
    /// </summary>
    /// <remarks>
    /// <b>Removed at S5.5</b> together with the panel host: "how to present a panel" is a
    /// View concern, not an instance fact.
    /// </remarks>
    public string? Surface { get; init; }
}
