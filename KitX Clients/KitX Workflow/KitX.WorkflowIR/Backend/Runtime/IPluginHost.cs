namespace KitX.Workflow.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// IPluginHost — the runtime abstraction over the host's plugin / service layer.
//
// The legacy runtime reached the concrete RealPluginManager and type-sniffed it
// (`is RealPluginManager realManager`) inside BlockScriptExecutionGlobals, coupling
// the runtime to the host implementation. The new runtime takes an IPluginHost
// instead: the host (Dashboard / a test double) supplies the call surface, and the
// runtime has no dependency on any concrete plugin manager.
//
// The methods mirror the C-level builtin surface (PluginCall / PluginCallWithTarget /
// service lifecycle / queries). When no host is wired, ExecutionGlobals returns
// benign defaults so the codegen path still runs (and tests can exercise codegen
// without a real host).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The host-side plugin/service surface the runtime dispatches to. Implemented by
/// the real host (Dashboard) or a test double; injected into ExecutionGlobals.
/// </summary>
public interface IPluginHost
{
    /// <summary>Invoke a local plugin method.</summary>
    object? Call(string pluginName, string methodName, params object[] args);

    /// <summary>Invoke a plugin method on a remote target device.</summary>
    object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args);

    /// <summary>Look up a connected device by name (null when not found).</summary>
    object? TryGetDevice(string deviceName);

    // ── Plugin lifecycle ──
    bool StartPlugin(string pluginName);
    bool StopPlugin(string pluginName);

    // ── Workflow lifecycle ──
    bool StopWorkflow(string workflowId);
    string CreateWorkflow(string name, string source);
    bool RunWorkflow(string workflowId);

    // ── Queries (JSON strings) ──
    bool InstallPlugin(string kxpPath);
    string GetPluginInfoByName(string pluginName);
    string ListPluginNames();
    string ListWorkflows();
}
