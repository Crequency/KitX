namespace KitX.WorkflowV6.Backend.Runtime;

using Serilog;

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
        try { return AsJsonElement(PluginHost.Call(pluginName, methodName, args)); }
        catch (Exception ex)
        {
            // The null return is load-bearing (generated code treats it as "no value"),
            // but the failure itself must be audible: log with the full call context
            // (W-8). Without this a silently-failing plugin call looks like a null result.
            Log.Warning(ex, "[ExecutionGlobals] PluginCall failed for plugin '{PluginName}' method '{MethodName}' (args: {ArgCount})",
                pluginName, methodName, args.Length);
            return null;
        }
    }

    /// <summary>
    /// PluginNotify: sends a plugin method invocation WITHOUT waiting for a response.
    /// For void/side-effect plugin functions the workflow continues immediately.
    /// </summary>
    public void PluginNotify(string pluginName, string methodName, params object[] args)
    {
        if (PluginHost is null)
            return;
        try
        {
            PluginHost.Notify(pluginName, methodName, args);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ExecutionGlobals] PluginNotify failed for plugin '{PluginName}' method '{MethodName}' (args: {ArgCount})",
                pluginName, methodName, args.Length);
        }
    }

    /// <summary>PluginCallWithTarget: invokes a method on a target device's plugin.</summary>
    public object? PluginCallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args)
    {
        if (PluginHost is null) return null;
        try { return AsJsonElement(PluginHost.CallWithTarget(pluginName, methodName, targetDevice, args)); }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ExecutionGlobals] PluginCallWithTarget failed for plugin '{PluginName}' method '{MethodName}' target '{TargetDevice}' (args: {ArgCount})",
                pluginName, methodName, targetDevice, args.Length);
            return null;
        }
    }

    /// <summary>TryGetDevice: finds an online device by name.</summary>
    public object? TryGetDevice(string deviceName) => PluginHost?.TryGetDevice(deviceName);
}
