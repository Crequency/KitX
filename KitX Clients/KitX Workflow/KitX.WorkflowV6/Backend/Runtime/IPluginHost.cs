namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// IPluginHost — the host-side bridge for plugin/service calls from workflows.
//
// Injected into ExecutionGlobals at runtime. When null, all plugin/service
// methods return default values (null/false/"[]") — the workflow runs without
// a host, plugin calls simply produce no results.
//
// Ported from v5.1 KitX.WorkflowIR.Backend.Runtime.IPluginHost (same signature,
// covering plugin invocation + lifecycle + queries). The v5 workflow-lifecycle
// members (StopWorkflow / CreateWorkflow / RunWorkflow / ListWorkflows) were
// retired in the B5+B6+B7 cleanup — the v6 IR architecture has no run-by-id
// service, so those four are no longer part of the host contract.
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

    /// <summary>
    /// Sends a plugin method invocation WITHOUT waiting for a response (fire-and-forget).
    /// Use for void/side-effect plugin functions (e.g. "show a popup") so the workflow
    /// does not block on the plugin's response channel. The default interface
    /// implementation falls back to <see cref="Call"/> so simple test hosts keep working;
    /// production hosts must override it with a real one-way send.
    /// </summary>
    void Notify(string pluginName, string methodName, params object[] args)
        => Call(pluginName, methodName, args);

    /// <summary>Calls a method on a plugin running on a target device.</summary>
    object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args);

    /// <summary>Finds an online device by name. Returns null if not found.</summary>
    object? TryGetDevice(string deviceName);

    // ── Plugin lifecycle ──

    bool StartPlugin(string pluginName);
    bool StopPlugin(string pluginName);

    // ── Plugin installation ──

    bool InstallPlugin(string kxpPath);

    // ── Queries ──

    string GetPluginInfoByName(string pluginName);
    string ListPluginNames();
}
