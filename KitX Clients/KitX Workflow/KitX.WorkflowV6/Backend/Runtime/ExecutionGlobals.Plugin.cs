namespace KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// ExecutionGlobals.Plugin — plugin invocation entry points
// (PluginCall / PluginCallWithTarget / TryGetDevice).
// Partial of ExecutionGlobals (see ExecutionGlobals.cs).
// ─────────────────────────────────────────────────────────────────────────────

public partial class ExecutionGlobals
{
    /// <summary>PluginCall: invokes a method on a local plugin. Returns JsonElement.</summary>
    public object? PluginCall(string pluginName, string methodName, params object[] args)
    {
        if (PluginHost is null) return null;
        try { return AsJsonElement(PluginHost.Call(pluginName, methodName, args ?? [])); }
        catch { return null; }
    }

    /// <summary>PluginCallWithTarget: invokes a method on a target device's plugin.</summary>
    public object? PluginCallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args)
    {
        if (PluginHost is null) return null;
        try { return AsJsonElement(PluginHost.CallWithTarget(pluginName, methodName, targetDevice, args ?? [])); }
        catch { return null; }
    }

    /// <summary>TryGetDevice: finds an online device by name.</summary>
    public object? TryGetDevice(string deviceName) => PluginHost?.TryGetDevice(deviceName);
}
