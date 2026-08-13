namespace KitX.Core.Plugin;

using System;
using System.Linq;
using System.Text.Json;
using Kscript.CSharp.Parser.Core;
using Kscript.CSharp.Parser.Models;
using KitX.Core.Configuration;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Data;
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
// C-11: the 9 lifecycle/query functions previously raised NotImplementedException
// (workflow nodes crashed on use). They are now bridged to real services:
//   • plugin functions   → IPluginService (PluginsManager — registered in
//                          AddCoreServices, constructor-injected)
//   • workflow functions → IWorkflowManagementService / IWorkflowStorageService
//                          (implementations live in KitX.WorkflowV6, registered by
//                          AddKitXWorkflowV6 AFTER AddCoreServices — injected lazily
//                          so PluginHostAdapter construction can never fail on
//                          registration order)
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

    // Lazy: IWorkflowManagementService/IWorkflowStorageService are registered by
    // AddKitXWorkflowV6 (after AddCoreServices). Deferring resolution to first use
    // keeps PluginHostAdapter construction independent of that registration order.
    private readonly Lazy<IWorkflowManagementService>? _workflowManagement;

    private readonly Lazy<IWorkflowStorageService>? _workflowStorage;

    // Lazy: the DataStore built-in plugin is registered by AddKitXToolKit (after
    // AddCoreServices). Deferring resolution keeps PluginHostAdapter construction
    // independent of that registration order.
    private readonly Lazy<BuiltinDataStorePlugin>? _dataStorePlugin;

    // Lazy: the KitX.UI built-in plugin (panel runtime) — same lazy rationale.
    private readonly Lazy<BuiltinUiPlugin>? _uiPlugin;

    /// <summary>
    /// Creates an adapter over the given plugin manager, plugin service and
    /// lazily-resolved workflow services.
    /// </summary>
    public PluginHostAdapter(
        IPluginManager pluginManager,
        IPluginService? pluginService = null,
        Lazy<IWorkflowManagementService>? workflowManagement = null,
        Lazy<IWorkflowStorageService>? workflowStorage = null,
        Lazy<BuiltinDataStorePlugin>? dataStorePlugin = null,
        Lazy<BuiltinUiPlugin>? uiPlugin = null)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _pluginService = pluginService;
        _workflowManagement = workflowManagement;
        _workflowStorage = workflowStorage;
        _dataStorePlugin = dataStorePlugin;
        _uiPlugin = uiPlugin;
    }

    /// <summary>
    /// Calls a local plugin method. Returns the raw JSON response string (the plugin's
    /// wire format), which ExecutionGlobals.PluginCall normalizes to JsonElement.
    /// The reserved built-in DataStore plugin (<see cref="BuiltinDataStorePlugin.PluginName"/>)
    /// is intercepted here and routed to the DataStore service instead of the plugin pool.
    /// </summary>
    public object? Call(string pluginName, string methodName, params object[] args)
    {
        // Route the reserved built-in DataStore plugin before the real plugin pool.
        if (_dataStorePlugin is not null &&
            string.Equals(pluginName, BuiltinDataStorePlugin.PluginName, StringComparison.OrdinalIgnoreCase))
        {
            return _dataStorePlugin.Value.Invoke(methodName, args);
        }

        // Route the reserved built-in KitX.UI plugin (panel runtime) before the real pool.
        if (_uiPlugin is not null &&
            string.Equals(pluginName, BuiltinUiPlugin.PluginName, StringComparison.OrdinalIgnoreCase))
        {
            return _uiPlugin.Value.Invoke(methodName, args);
        }

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

    // ── Workflow lifecycle (C-11: bridged to workflow services) ──

    public bool StopWorkflow(string workflowId)
    {
        if (_workflowManagement is null)
            return false;

        try
        {
            return _workflowManagement.Value.StopWorkflowAsync(workflowId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] StopWorkflow failed for '{WorkflowId}'", workflowId);
            return false;
        }
    }

    /// <summary>
    /// Creates a workflow. The storage contract creates an empty-IR workflow
    /// (<see cref="IWorkflowStorageService.CreateWorkflowAsync"/>); the node's
    /// <paramref name="source"/> text is carried in the workflow description because
    /// KcsFileFormat v2 stores IR only (KS/BP text are projections). Returns the
    /// new workflow's Id.
    /// </summary>
    public string CreateWorkflow(string name, string source)
    {
        if (_workflowStorage is null)
            return string.Empty;

        try
        {
            var created = _workflowStorage.Value
                .CreateWorkflowAsync(name, description: source)
                .GetAwaiter().GetResult();
            return created.Id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] CreateWorkflow failed for '{Name}'", name);
            return string.Empty;
        }
    }

    public bool RunWorkflow(string workflowId)
    {
        if (_workflowManagement is null)
            return false;

        try
        {
            return _workflowManagement.Value.RunWorkflowAsync(workflowId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] RunWorkflow failed for '{WorkflowId}'", workflowId);
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

    /// <summary>
    /// Returns stored workflow Ids as a JSON array string (e.g. <c>["id1","id2"]</c>).
    /// </summary>
    public string ListWorkflows()
    {
        if (_workflowStorage is null)
            return "[]";

        try
        {
            var workflows = _workflowStorage.Value.DiscoverWorkflowsAsync().GetAwaiter().GetResult();
            var ids = workflows.Select(w => w.Id).ToList();

            return JsonSerializer.Serialize(ids);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginHostAdapter] ListWorkflows failed");
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
