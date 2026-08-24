namespace KitX.ToolKit.Instances;

/// <summary>
/// Reserved constant injected into every workflow of a spawned instance, carrying the
/// instance's id. The name is defined in WorkflowV6 (<see cref="KitX.WorkflowV6.ToolKitConstants.InstanceId"/>)
/// and injected per-run into <c>ExecutionGlobals.InstanceId</c> by the execution backend, so
/// workflow authors no longer declare or pass it manually.
/// </summary>
public static class InstanceConstants
{
    /// <summary>Reserved constant carrying the instance id.</summary>
    public const string InstanceId = KitX.WorkflowV6.ToolKitConstants.InstanceId;

    /// <summary>
    /// The spawn trigger's <c>Surface</c> value meaning "run in the background" (a badge
    /// hints it; only surfaced when the workflow calls <c>KitX.UI.OpenPanel</c> or the user
    /// opens it manually). Centralized here so the magic string is defined once.
    /// </summary>
    public const string SurfaceSilent = "silent";

    /// <summary>
    /// Run-monitor counter summary format (<c>{0}</c>=active, <c>{1}</c>=completed,
    /// <c>{2}</c>=failed). Library-side i18n is deferred — the text stays Chinese for now and
    /// will be localized as part of the G26 library-side i18n follow-up. No resx is introduced.
    /// </summary>
    public const string RunSummaryFormat = "运行 {0} / 完成 {1} / 失败 {2}";
}
