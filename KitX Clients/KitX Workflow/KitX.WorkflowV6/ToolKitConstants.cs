namespace KitX.WorkflowV6;

// ─────────────────────────────────────────────────────────────────────────────
// ToolKitConstants — reserved names shared between WorkflowV6 and host-side
// ToolKit builtins.
//
// The instance id is injected per-run by the execution backend (from the constant
// overrides) into ExecutionGlobals.InstanceId, so workflow authors no longer
// declare or pass the __KitXInstanceId__ constant themselves. KitX.ToolKit's
// InstanceConstants references this single source of truth.
//
// There used to be a second constant here, OutputNamespace, which carried a
// DataStore namespace for FlowOut to write into. Q48 removed that channel —
// FlowOut collects into the globals instance and the values ride home on the
// execution result — so the constant is gone rather than left unused.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Reserved constant names shared between WorkflowV6 and host-side ToolKit builtins.</summary>
public static class ToolKitConstants
{
    /// <summary>Reserved constant carrying the owning instance's id, injected per-run.</summary>
    public const string InstanceId = "__KitXInstanceId__";
}
