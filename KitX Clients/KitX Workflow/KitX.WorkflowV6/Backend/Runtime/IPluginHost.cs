namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// IPluginHost — the host-side bridge for plugin/service calls from workflows.
//
// Injected into ExecutionGlobals at runtime. When null, all plugin/service
// methods return default values (null/false/"[]") — the workflow runs without
// a host, plugin calls simply produce no results.
//
// Ported from v5.1 KitX.WorkflowIR.Backend.Runtime.IPluginHost (same signature,
// same 12 methods covering plugin invocation + lifecycle + queries).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Provides plugin invocation and service management capabilities to the workflow
/// runtime. Implementations bridge to the Dashboard's plugin manager / device
/// manager / workflow manager.
/// </summary>
public interface IPluginHost
{
    // ── Plugin invocation ──

    /// <summary>Calls a method on a local plugin. Returns the JSON result.</summary>
    object? Call(string pluginName, string methodName, params object[] args);

    /// <summary>Calls a method on a plugin running on a target device.</summary>
    object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args);

    /// <summary>Finds an online device by name. Returns null if not found.</summary>
    object? TryGetDevice(string deviceName);

    // ── Plugin lifecycle ──

    bool StartPlugin(string pluginName);
    bool StopPlugin(string pluginName);

    // ── Workflow lifecycle ──

    bool StopWorkflow(string workflowId);
    string CreateWorkflow(string name, string source);
    bool RunWorkflow(string workflowId);

    // ── Plugin installation ──

    bool InstallPlugin(string kxpPath);

    // ── Queries ──

    string GetPluginInfoByName(string pluginName);
    string ListPluginNames();
    string ListWorkflows();
}
