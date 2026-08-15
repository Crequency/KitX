namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.ToolKit — first-class builtin entry points for the ToolKit
// host-side services (KitX.UI panel runtime + KitX.DataStore blackboard).
//
// These are ordinary builtin functions (dispatch is by method-name convention:
// codegen emits `this.UiSet(...)`), so workflow authors write `UiSet("input", "hello")`
// instead of the old `PluginCall("KitX.UI", "Set", instanceId, "input", "hello")`.
//
// Each method routes through IPluginHost.Call with the reserved plugin name; the
// host's PluginHostAdapter intercepts the reserved name and bridges to the ToolKit
// service (the internal seam — invisible to workflow authors). The UI family needs
// the owning instance id (auto-injected into <see cref="InstanceId"/> by the
// execution backend); the DataStore family does not.
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    /// <summary>
    /// The owning instance's id, injected per-run by the execution backend (from the
    /// constant overrides). Null when the workflow runs outside a ToolKit instance.
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// The workflow's instance-scoped DataStore output namespace (Bench scheduler
    /// injected). Null outside a ToolKit instance; <see cref="BenchOut"/> no-ops then.
    /// </summary>
    public string? OutputNamespace { get; set; }

    /// <summary>
    /// The raw run-time constant overrides (resolved trigger binding params included),
    /// injected per-run by the execution backend. Null outside a ToolKit instance;
    /// <see cref="BenchIn"/> returns its default then.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? RawOverrides { get; set; }

    // ── KitX.UI family (panel runtime) — instance-scoped. ──

    /// <summary>UiSet(controlId, value) → main property key.</summary>
    public object? UiSet(string controlId, object? value)
    {
        if (PluginHost is null || InstanceId is null) return null;
        return PluginHost.Call("KitX.UI", "Set", new object[] { InstanceId, controlId, value! });
    }

    /// <summary>UiSet(controlId, prop, value) → panel/{controlId}/{prop}.</summary>
    public object? UiSet(string controlId, string prop, object? value)
    {
        if (PluginHost is null || InstanceId is null) return null;
        return PluginHost.Call("KitX.UI", "Set", new object[] { InstanceId, controlId, prop, value! });
    }

    /// <summary>UiGet(controlId) → reads the control's main property key.</summary>
    public object? UiGet(string controlId)
    {
        if (PluginHost is null || InstanceId is null) return null;
        return PluginHost.Call("KitX.UI", "Get", new object[] { InstanceId, controlId });
    }

    /// <summary>UiLog(controlId, entry) → appends to the named log control.</summary>
    public object? UiLog(string controlId, object? entry)
    {
        if (PluginHost is null || InstanceId is null) return null;
        return PluginHost.Call("KitX.UI", "Log", new object[] { InstanceId, controlId, entry! });
    }

    /// <summary>UiLog(entry) → appends to the default "log" control.</summary>
    public object? UiLog(object? entry)
    {
        if (PluginHost is null || InstanceId is null) return null;
        return PluginHost.Call("KitX.UI", "Log", new object[] { InstanceId, entry! });
    }

    /// <summary>UiProgress(controlId, value) → sets the progress control's value.</summary>
    public object? UiProgress(string controlId, object? value)
    {
        if (PluginHost is null || InstanceId is null) return null;
        return PluginHost.Call("KitX.UI", "Progress", new object[] { InstanceId, controlId, value! });
    }

    /// <summary>UiDialog(controlId, message, buttons...) → writes the dialog request slot.</summary>
    public object? UiDialog(string controlId, string message, params string[] buttons)
    {
        if (PluginHost is null || InstanceId is null) return null;
        var args = new object[3 + buttons.Length];
        args[0] = InstanceId;
        args[1] = controlId;
        args[2] = message;
        for (int i = 0; i < buttons.Length; i++) args[3 + i] = buttons[i];
        return PluginHost.Call("KitX.UI", "Dialog", args);
    }

    /// <summary>UiOpenPanel() → requests the host to present the instance's panel.</summary>
    public object? UiOpenPanel()
    {
        if (PluginHost is null || InstanceId is null) return null;
        return PluginHost.Call("KitX.UI", "OpenPanel", new object[] { InstanceId });
    }

    // ── KitX.DataStore family (data blackboard) — not instance-scoped. ──

    /// <summary>DataStoreSet(key, value) → writes a blackboard key.</summary>
    public object? DataStoreSet(string key, object? value)
    {
        if (PluginHost is null) return null;
        return PluginHost.Call("KitX.DataStore", "Set", new object[] { key, value! });
    }

    /// <summary>DataStoreGet(key) → reads a blackboard key.</summary>
    public object? DataStoreGet(string key)
    {
        if (PluginHost is null) return null;
        return PluginHost.Call("KitX.DataStore", "Get", new object[] { key });
    }

    /// <summary>DataStoreWait(keys...) → blocks until all keys are set (AND).</summary>
    public object? DataStoreWait(params string[] keys)
    {
        if (PluginHost is null) return null;
        return PluginHost.Call("KitX.DataStore", "Wait", keys);
    }

    /// <summary>DataStoreWaitAny(keys...) → blocks until any key is set (OR).</summary>
    public object? DataStoreWaitAny(params string[] keys)
    {
        if (PluginHost is null) return null;
        return PluginHost.Call("KitX.DataStore", "WaitAny", keys);
    }

    /// <summary>DataStoreRemove(key) → removes a blackboard key.</summary>
    public object? DataStoreRemove(string key)
    {
        if (PluginHost is null) return null;
        return PluginHost.Call("KitX.DataStore", "Remove", new object[] { key });
    }

    /// <summary>DataStoreKeys() → lists all blackboard keys.</summary>
    public object? DataStoreKeys()
    {
        if (PluginHost is null) return null;
        return PluginHost.Call("KitX.DataStore", "Keys");
    }

    /// <summary>DataStoreContains(key) → whether a blackboard key exists.</summary>
    public object? DataStoreContains(string key)
    {
        if (PluginHost is null) return null;
        return PluginHost.Call("KitX.DataStore", "Contains", new object[] { key });
    }
}
