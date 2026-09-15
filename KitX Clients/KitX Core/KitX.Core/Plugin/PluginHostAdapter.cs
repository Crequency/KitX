namespace KitX.Core.Plugin;

using System;
using System.Linq;
using System.Text.Json;
using Kscript.CSharp.Parser.Core;
using Kscript.CSharp.Parser.Models;
using KitX.Core.Configuration;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Plugin;
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
// C-11: the lifecycle/query functions previously raised NotImplementedException
// (workflow nodes crashed on use). They are now bridged to real services:
//   • plugin functions → IPluginService (PluginsManager — registered in
//                        AddCoreServices, constructor-injected)
// The v5 workflow-lifecycle functions (StopWorkflow / CreateWorkflow / RunWorkflow /
// ListWorkflows) were retired in the B5+B6+B7 cleanup — the v6 IR architecture has
// no run-by-id service, so they are no longer part of the IPluginHost contract and
// no longer bridged here.
// TryGetDevice stays null — it is the interface's documented "device not found"
// result. All bridge methods swallow failures and return their safe default
// (false / "" / "[]"), so a failing node yields a visible false/empty result
// instead of throwing into the generated workflow code.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Adapts <see cref="RealPluginManager"/> to the WorkflowV6 <see cref="IPluginHost"/>
/// contract. Registered as a singleton in DI (see CoreServiceCollectionExtensions).
/// </summary>
public sealed class PluginHostAdapter : KitX.WorkflowV6.Backend.Runtime.IPluginHost
{
    private readonly IPluginManager _pluginManager;

    private readonly IPluginService? _pluginService;

    /// <summary>
    /// Creates an adapter over the given plugin manager and plugin service.
    /// </summary>
    public PluginHostAdapter(
        IPluginManager pluginManager,
        IPluginService? pluginService = null)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _pluginService = pluginService;
    }

    /// <summary>
    /// Calls a local plugin method. Returns the raw JSON response string (the plugin's
    /// wire format), which ExecutionGlobals.PluginCall normalizes to JsonElement.
    /// </summary>
    public object? Call(string pluginName, string methodName, params object[] args)
    {
        // Transition shim: the reserved "KitX.UI" / "KitX.DataStore" names previously
        // routed to the ToolKit services through this adapter. Those services are now
        // exposed as first-class builtins on ToolKitExecutionGlobals (the factory-registered
        // ExecutionGlobals subclass), so a workflow reaching them by reserved plugin name is
        // a legacy/out-of-host call — warn and degrade to null (safe-default) rather than
        // silently doing nothing.
        if (string.Equals(pluginName, "KitX.UI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pluginName, "KitX.DataStore", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "[PluginHostAdapter] Reserved builtin '{Plugin}' is no longer a plugin call — " +
                "use the first-class Ui*/DataStore* workflow functions instead. Returning null.",
                pluginName);
            return null;
        }

        var callInfo = BuildCallInfo(pluginName, methodName, args);
        Log.Information("[PluginHostAdapter] PluginCall begin {Plugin}.{Method}", pluginName, methodName);
        try
        {
            // Call<string> returns the raw JSON response string (SendPluginRequest special-cases T=string).
            // Returning the string lets AsJsonElement parse it into a JsonElement tree.
            var result = _pluginManager.Call<string>(callInfo);
            Log.Information("[PluginHostAdapter] PluginCall end {Plugin}.{Method}", pluginName, methodName);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] Call failed: {Plugin}.{Method}", pluginName, methodName);
            return null;
        }
    }

    /// <inheritdoc/>
    public void Notify(string pluginName, string methodName, params object[] args)
    {
        var callInfo = BuildCallInfo(pluginName, methodName, args);
        Log.Information("[PluginHostAdapter] PluginNotify fire-and-forget {Plugin}.{Method}", pluginName, methodName);
        _pluginManager.Notify(callInfo);
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

    // ── Plugin lifecycle (C-11: bridged to IPluginService / PluginsManager) ──

    public bool StartPlugin(string pluginName)
    {
        if (_pluginService is null)
            return false;

        try
        {
            var plugin = FindPluginByName(pluginName);
            if (plugin is null)
            {
                Log.Warning("[PluginHostAdapter] StartPlugin: plugin '{PluginName}' not installed", pluginName);
                return false;
            }

            return _pluginService.StartPluginAsync(plugin.Id).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] StartPlugin failed for '{PluginName}'", pluginName);
            return false;
        }
    }

    public bool StopPlugin(string pluginName)
    {
        if (_pluginService is null)
            return false;

        try
        {
            var plugin = FindPluginByName(pluginName);
            if (plugin is null)
            {
                Log.Warning("[PluginHostAdapter] StopPlugin: plugin '{PluginName}' not installed", pluginName);
                return false;
            }

            return _pluginService.StopPluginAsync(plugin.Id).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] StopPlugin failed for '{PluginName}'", pluginName);
            return false;
        }
    }

    // ── Plugin installation (C-11) ──

    public bool InstallPlugin(string kxpPath)
    {
        if (_pluginService is null)
            return false;

        try
        {
            return _pluginService.ImportPluginAsync(kxpPath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] InstallPlugin failed for '{KxpPath}'", kxpPath);
            return false;
        }
    }

    // ── Queries (C-11) ──

    /// <summary>
    /// Returns the installed plugin's <see cref="PluginInfo"/> serialized as JSON
    /// (network wire options), or "" if not installed.
    /// </summary>
    public string GetPluginInfoByName(string pluginName)
    {
        if (_pluginService is null)
            return string.Empty;

        try
        {
            var plugin = FindPluginByName(pluginName);
            if (plugin?.PluginInfo is null)
                return string.Empty;

            return JsonSerializer.Serialize(plugin.PluginInfo, NetworkSerialization.Options);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] GetPluginInfoByName failed for '{PluginName}'", pluginName);
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns installed plugin names as a JSON array string (e.g. <c>["a","b"]</c>).
    /// </summary>
    public string ListPluginNames()
    {
        if (_pluginService is null)
            return "[]";

        try
        {
            var names = _pluginService.GetInstalledPlugins()
                .Select(p => p.PluginInfo?.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            return JsonSerializer.Serialize(names);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] ListPluginNames failed");
            return "[]";
        }
    }

    // ── Helpers ──

    private IPluginInstallation? FindPluginByName(string pluginName)
    {
        if (string.IsNullOrEmpty(pluginName) || _pluginService is null)
            return null;

        return _pluginService.GetInstalledPlugins()
            .FirstOrDefault(p => p.PluginInfo?.Name == pluginName);
    }

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
