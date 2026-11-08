namespace KitX.WorkflowV6;

// ─────────────────────────────────────────────────────────────────────────────
// ToolKitConstants — reserved names shared between WorkflowV6 and host-side
// ToolKit builtins.
//
// The ToolKit id is injected per-run by the execution backend (from the constant
// overrides) into ExecutionGlobals.ToolkitId, so workflow authors no longer
// declare or pass the __KitXToolkitId__ constant themselves. KitX.ToolKit's
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
    /// <summary>
    /// Reserved constant carrying the owning ToolKit's id, injected per-run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It used to be <c>__KitXInstanceId__</c>, and the id was the run's own — which then had to
    /// be resolved through the instance table to reach the ToolKit. That lookup could return null
    /// for a run whose ToolKit was still mounted (the instance had been evicted), and the resulting
    /// error said "the toolkit is not mounted", which was not true.
    /// </para>
    /// <para>
    /// Carrying the ToolKit id directly removes the lookup: the key table depends on whether the
    /// ToolKit is mounted and on nothing else, so there is one fact to be wrong about instead of
    /// two. Flow identity is still available separately where it is genuinely needed.
    /// </para>
    /// </remarks>
    public const string ToolkitId = "__KitXToolkitId__";
}
