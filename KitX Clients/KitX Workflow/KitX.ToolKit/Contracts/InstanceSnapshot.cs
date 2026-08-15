using KitX.ToolKit.Instances;

namespace KitX.ToolKit.Contracts;

/// <summary>
/// A read-only snapshot of a running/completed <see cref="Instances.ToolkitInstance"/>,
/// for the run monitor / remote directory (ToolKit 实例模型定稿 D4/D6). Immutable and
/// serializable so it can cross the contract boundary (desktop adapter, future remote API).
/// </summary>
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
    /// The spawn trigger's presentation mode (<c>auto</c>/<c>silent</c>), resolved by the
    /// instance manager. Null when unknown (e.g. an older caller constructing the snapshot).
    /// </summary>
    public string? Surface { get; init; }

    /// <summary>True when this instance spawned with <c>Surface=silent</c> (tree badge, C28).</summary>
    public bool IsSilent => string.Equals(Surface, "silent", StringComparison.OrdinalIgnoreCase);

    /// <summary>Human-readable run-counter summary for the run monitor.</summary>
    public string RunSummary => $"运行 {ActiveRuns} / 完成 {CompletedRuns} / 失败 {FailedRuns}";
}
