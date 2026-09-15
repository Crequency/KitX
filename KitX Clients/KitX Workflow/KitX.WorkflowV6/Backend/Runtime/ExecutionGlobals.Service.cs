namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.Service — plugin lifecycle management functions
// (StartPlugin / StopPlugin / InstallPlugin / GetPluginInfoByName /
// ListPluginNames).
//
// The v5 workflow-lifecycle functions (StopWorkflow / CreateWorkflow /
// RunWorkflow / ListWorkflows) were retired in the B5+B6+B7 cleanup — the v6 IR
// architecture has no run-by-id service (IExecutionBackend takes a Workflow, not
// a workflowId), and the WorkflowSessionManager that bridged that gap was removed
// along with them. Workflow orchestration now lives entirely in the host layer
// (KitX.ToolKit / KitX.Core), not in the engine's ExecutionGlobals surface.
//
// Partial of ExecutionGlobals (see ExecutionGlobals.cs).
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    // ── Plugin lifecycle ──

    public bool StartPlugin(string pluginName) => PluginHost?.StartPlugin(pluginName) ?? false;
    public bool StopPlugin(string pluginName) => PluginHost?.StopPlugin(pluginName) ?? false;

    // ── Plugin installation ──

    public bool InstallPlugin(string kxpPath) => PluginHost?.InstallPlugin(kxpPath) ?? false;

    // ── Queries ──

    public string GetPluginInfoByName(string pluginName) => PluginHost?.GetPluginInfoByName(pluginName) ?? "";
    public string ListPluginNames() => PluginHost?.ListPluginNames() ?? "[]";
}
