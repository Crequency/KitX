namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.Service — plugin / workflow lifecycle management functions
// (StartPlugin / StopPlugin / StopWorkflow / CreateWorkflow / RunWorkflow /
// InstallPlugin / GetPluginInfoByName / ListPluginNames / ListWorkflows).
// Partial of ExecutionGlobals (see ExecutionGlobals.cs).
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    // ── Plugin lifecycle ──

    public bool StartPlugin(string pluginName) => PluginHost?.StartPlugin(pluginName) ?? false;
    public bool StopPlugin(string pluginName) => PluginHost?.StopPlugin(pluginName) ?? false;

    // ── Workflow lifecycle ──

    public bool StopWorkflow(string workflowId) => PluginHost?.StopWorkflow(workflowId) ?? false;
    public string CreateWorkflow(string name, string source) => PluginHost?.CreateWorkflow(name, source) ?? "";
    public bool RunWorkflow(string workflowId) => PluginHost?.RunWorkflow(workflowId) ?? false;

    // ── Plugin installation ──

    public bool InstallPlugin(string kxpPath) => PluginHost?.InstallPlugin(kxpPath) ?? false;

    // ── Queries ──

    public string GetPluginInfoByName(string pluginName) => PluginHost?.GetPluginInfoByName(pluginName) ?? "";
    public string ListPluginNames() => PluginHost?.ListPluginNames() ?? "[]";
    public string ListWorkflows() => PluginHost?.ListWorkflows() ?? "[]";
}
