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
// Lifecycle/query methods (StartPlugin/StopPlugin/etc.) are stubbed for now — plugin
// lifecycle is managed elsewhere (PluginsManager). They previously returned benign
// defaults so workflow scripts that call them didn't crash, but a workflow then saw a
// fake success / empty result that was harder to diagnose than a failure. Since the
// adapter has no implementation to offer, they now log a warning and raise
// NotImplementedException so the workflow surfaces a real error (D6).
// TryGetDevice is the exception: null is the documented "not found" result, so it
// keeps its truthful (if unhelpful) return value.
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

    /// <summary>Looks up a connected device by name. Not implemented — null is the
    /// interface's documented "device not found" result, so it is returned truthfully.</summary>
    public object? TryGetDevice(string deviceName) => null;

    // ── Plugin lifecycle (stubbed — managed by PluginsManager) ──
    //
    // D6: these raise instead of silently returning fake success. The workflow runtime
    // does not swallow exceptions for these methods, so a calling workflow fails with
    // a visible error rather than continuing on a false result.

    public bool StartPlugin(string pluginName) => StubNotImplemented(nameof(StartPlugin));
    public bool StopPlugin(string pluginName) => StubNotImplemented(nameof(StopPlugin));

    // ── Workflow lifecycle (stubbed) ──

    public bool StopWorkflow(string workflowId) => StubNotImplemented(nameof(StopWorkflow));
    public string CreateWorkflow(string name, string source) => StubNotImplemented<string>(nameof(CreateWorkflow));
    public bool RunWorkflow(string workflowId) => StubNotImplemented(nameof(RunWorkflow));

    // ── Queries (stubbed) ──

    public bool InstallPlugin(string kxpPath) => StubNotImplemented(nameof(InstallPlugin));
    public string GetPluginInfoByName(string pluginName) => StubNotImplemented<string>(nameof(GetPluginInfoByName));
    public string ListPluginNames() => StubNotImplemented<string>(nameof(ListPluginNames));
    public string ListWorkflows() => StubNotImplemented<string>(nameof(ListWorkflows));

    // ── Helpers ──

    /// <summary>
    /// Logs a warning and throws for adapter methods that have no implementation.
    /// Replaces the old silent fake defaults (false/""/"{}") that made workflow
    /// failures harder to diagnose than an explicit error (D6).
    /// </summary>
    private static T StubNotImplemented<T>(string methodName)
    {
        Log.Warning("[PluginHostAdapter] {Method} is a stub — no implementation in the active host; raising instead of returning a fake result", methodName);
        throw new NotImplementedException(
            $"{nameof(PluginHostAdapter)}.{methodName} is not implemented — plugin lifecycle/querying is managed outside the workflow host.");
    }

    private static bool StubNotImplemented(string methodName) => StubNotImplemented<bool>(methodName);

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
