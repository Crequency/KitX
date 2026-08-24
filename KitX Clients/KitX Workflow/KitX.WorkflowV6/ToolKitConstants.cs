namespace KitX.WorkflowV6;

// ─────────────────────────────────────────────────────────────────────────────
// ToolKitConstants — reserved names shared between WorkflowV6 and host-side
// ToolKit builtins (KitX.UI / KitX.DataStore).
//
// The instance id is injected per-run by the execution backend (from the constant
// overrides) into ExecutionGlobals.InstanceId, so workflow authors no longer
// declare or pass the __KitXInstanceId__ constant themselves. KitX.ToolKit's
// InstanceConstants references this single source of truth.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Reserved constant names shared between WorkflowV6 and host-side ToolKit builtins.</summary>
public static class ToolKitConstants
{
    /// <summary>Reserved constant carrying the owning instance's id, injected per-run.</summary>
    public const string InstanceId = "__KitXInstanceId__";

    /// <summary>
    /// Reserved override key carrying the workflow's instance-scoped DataStore output
    /// namespace (e.g. <c>{toolkitId}/{instanceId}/wf/{workflowId}</c>), injected by the
    /// Bench scheduler. <c>BenchOut</c> reads it via <see cref="Backend.ToolKitRunContext"/>
    /// so workflow authors no longer declare the constant or build the key by hand.
    /// </summary>
    public const string OutputNamespace = "__KitXDataStoreOutputNamespace__";
}
