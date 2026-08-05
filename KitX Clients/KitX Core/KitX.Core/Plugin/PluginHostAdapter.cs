namespace KitX.Core.Plugin;

using System;
using Kscript.CSharp.Parser.Core;
using Kscript.CSharp.Parser.Models;
using KitX.WorkflowV6.Backend.Runtime;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// PluginHostAdapter — bridges the active RealPluginManager (the
// Kscript.CSharp.Parser plugin system) to the WorkflowV6 IPluginHost contract.
// (The v5.1 WorkflowIR contract was archived; only the v6 contract remains.)
// Registered as a singleton in DI (see CoreServiceCollectionExtensions) so v6
// PluginCall builtins can invoke real plugins at runtime.
//
// It wraps RealPluginManager.Call<string>, which returns the raw JSON response
// string. ExecutionGlobals.PluginCall then normalizes that string to a
// JsonElement via AsJsonElement (List-Port-And-Json-
// Functions-Design.md §1).
//
// Lifecycle methods (StartPlugin/StopPlugin/etc.) are stubbed for now — plugin
// lifecycle is managed elsewhere (PluginsManager). They return benign defaults
// so workflow scripts that call them don't crash.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Adapts <see cref="RealPluginManager"/> to the WorkflowV6 <see cref="IPluginHost"/>
/// contract. Registered as a singleton in DI (see CoreServiceCollectionExtensions).
/// </summary>
public sealed class PluginHostAdapter : KitX.WorkflowV6.Backend.Runtime.IPluginHost
{
    private readonly IPluginManager _pluginManager;

    /// <summary>Creates an adapter over the given plugin manager.</summary>
    public PluginHostAdapter(IPluginManager pluginManager)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
    }

    /// <summary>
    /// Calls a local plugin method. Returns the raw JSON response string (the plugin's
    /// wire format), which ExecutionGlobals.PluginCall normalizes to JsonElement.
    /// </summary>
    public object? Call(string pluginName, string methodName, params object[] args)
    {
        var callInfo = BuildCallInfo(pluginName, methodName, args);
        try
        {
            // Call<string> returns the raw JSON response string (SendPluginRequest special-cases T=string).
            // Returning the string lets AsJsonElement parse it into a JsonElement tree.
            return _pluginManager.Call<string>(callInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] Call failed: {Plugin}.{Method}", pluginName, methodName);
            return null;
        }
    }

    /// <summary>
    /// Calls a plugin method on a remote device. Currently delegates to the same path as
    /// Call (device routing is the host's responsibility via IPluginServiceProvider).
    /// </summary>
    public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args)
    {
        // The active RealPluginManager does not yet distinguish target devices in its Call path;
        // device routing is handled by the plugin service provider's connector lookup. For now
        // we pass through with the targetDevice encoded as a leading arg so plugins can read it.
        var fullArgs = new object[args.Length + 1];
        fullArgs[0] = targetDevice;
        Array.Copy(args, 0, fullArgs, 1, args.Length);
        return Call(pluginName, methodName, fullArgs);
    }

    /// <summary>Looks up a connected device by name. Not yet implemented — returns null.</summary>
    public object? TryGetDevice(string deviceName) => null;

    // ── Plugin lifecycle (stubbed — managed by PluginsManager) ──

    public bool StartPlugin(string pluginName) => false;
    public bool StopPlugin(string pluginName) => false;

    // ── Workflow lifecycle (stubbed) ──

    public bool StopWorkflow(string workflowId) => false;
    public string CreateWorkflow(string name, string source) => "";
    public bool RunWorkflow(string workflowId) => false;

    // ── Queries (stubbed) ──

    public bool InstallPlugin(string kxpPath) => false;
    public string GetPluginInfoByName(string pluginName) => "{}";
    public string ListPluginNames() => "[]";
    public string ListWorkflows() => "[]";

    // ── Helpers ──

    private static PluginCallInfo BuildCallInfo(string pluginName, string methodName, object[] args)
    {
        var parameters = args ?? Array.Empty<object>();
        var paramTypes = new Type[parameters.Length];
        var paramNames = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            paramTypes[i] = parameters[i]?.GetType() ?? typeof(object);
            paramNames[i] = $"arg{i}";
        }
        return new PluginCallInfo(pluginName, methodName, parameters, paramTypes, paramNames);
    }
}

/// <summary>
/// A no-op IPluginManager used as a fallback when no real plugin service provider is
/// registered. All calls return defaults; IsPluginExists returns false. This lets the
/// workflow DI pipeline resolve IPluginHost (via PluginHostAdapter) without a live
/// plugin connection — PluginCall builtins will log a warning and return null at runtime.
/// </summary>
internal sealed class NoOpPluginManager : IPluginManager
{
    public T Call<T>(PluginCallInfo callInfo) => default!;
    public void Call(PluginCallInfo callInfo) { }
    public bool IsPluginExists(string pluginName) => false;
    public bool IsMethodExists(string pluginName, string methodName) => false;
}
