namespace KitX.ToolKit.Instances;

/// <summary>
/// Lifecycle state of a <see cref="ToolkitInstance"/> (ToolKit 实例模型定稿 D6).
/// <list type="bullet">
///   <item><b>Running</b> — at least one workflow chain is in flight.</item>
///   <item><b>Completed</b> — every chain finished; the instance is retained for review
///   until the user explicitly ends it.</item>
/// </list>
/// An ended instance is removed from the manager and no longer observable.
/// </summary>
public enum InstanceStatus
{
    /// <summary>At least one workflow chain is running.</summary>
    Running,

    /// <summary>All chains finished; retained until manually ended.</summary>
    Completed,
}
