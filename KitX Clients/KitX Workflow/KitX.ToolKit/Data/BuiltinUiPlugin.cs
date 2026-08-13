using KitX.ToolKit.Instances;
using KitX.ToolKit.Panels;

namespace KitX.ToolKit.Data;

/// <summary>
/// Exposes the panel runtime to workflows as a built-in plugin (the reserved name
/// <c>KitX.UI</c>), mirroring how <see cref="BuiltinDataStorePlugin"/> exposes the DataStore.
/// A workflow calls it like a plugin function — e.g.
/// <c>PluginCall("KitX.UI", "Set", instanceId, "input", "hello")</c> — reusing the existing
/// <c>PluginCall</c> path. The host's <c>IPluginHost</c> implementation intercepts the
/// reserved name and routes here, so <b>KitX.WorkflowV6 itself is untouched</b>.
///
/// <para>Every method takes the instance id as its first argument (read from the injected
/// <see cref="InstanceConstants.InstanceId"/> constant), so the panel keys are scoped to the
/// owning instance. Function family: Set / Get / Log / Progress / Dialog / OpenPanel.</para>
/// </summary>
public sealed class BuiltinUiPlugin
{
    /// <summary>The reserved plugin name a workflow uses to reach the panel runtime.</summary>
    public const string PluginName = "KitX.UI";

    private readonly PanelRuntime _panelRuntime;
    private readonly DataStore _dataStore;
    private readonly ToolkitInstanceManager _manager;

    public BuiltinUiPlugin(PanelRuntime panelRuntime, DataStore dataStore, ToolkitInstanceManager manager)
    {
        _panelRuntime = panelRuntime ?? throw new ArgumentNullException(nameof(panelRuntime));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>Dispatches a plugin function call onto the panel runtime. Method names are
    /// case-insensitive; unknown methods return null (safe-default convention).</summary>
    public object? Invoke(string methodName, object?[]? args)
    {
        var name = methodName?.ToLowerInvariant();
        return name switch
        {
            "set" => Set(args),
            "get" => Get(args),
            "log" => Log(args),
            "progress" => Progress(args),
            "dialog" => Dialog(args),
            "openpanel" => OpenPanel(args),
            _ => null,
        };
    }

    /// <summary>Returns true when this plugin provides <paramref name="methodName"/>.</summary>
    public bool HasMethod(string methodName)
        => methodName?.ToLowerInvariant() is "set" or "get" or "log" or "progress" or "dialog" or "openpanel";

    private object? Set(object?[]? args)
    {
        // Set(instanceId, controlId, value) → main property key
        // Set(instanceId, controlId, prop, value) → panel/{controlId}/{prop}
        if (args is null || args.Length < 3 || args[0] is not string instanceId || args[1] is not string controlId)
            return null;

        if (args.Length >= 4 && args[2] is string prop)
        {
            var toolkitId = _manager.GetToolkitId(instanceId);
            if (toolkitId is null)
                return null;
            _dataStore.Set(PanelScope.Key(toolkitId, instanceId, controlId, prop), args[3]);
            return true;
        }

        _panelRuntime.SetControlValue(instanceId, controlId, args[2]);
        return true;
    }

    private object? Get(object?[]? args)
    {
        if (args is null || args.Length < 2 || args[0] is not string instanceId || args[1] is not string controlId)
            return null;
        return _panelRuntime.GetControlValue(instanceId, controlId);
    }

    private object? Log(object?[]? args)
    {
        // Log(instanceId, controlId?, entry) — controlId defaults to "log".
        if (args is null || args.Length < 2 || args[0] is not string instanceId)
            return null;

        string controlId;
        object? entry;
        if (args.Length >= 3)
        {
            controlId = args[1] as string ?? "log";
            entry = args[2];
        }
        else
        {
            controlId = "log";
            entry = args[1];
        }

        var toolkitId = _manager.GetToolkitId(instanceId);
        if (toolkitId is null)
            return null;
        _dataStore.Append(PanelScope.Key(toolkitId, instanceId, controlId, "log"), entry);
        return true;
    }

    private object? Progress(object?[]? args)
    {
        if (args is null || args.Length < 3 || args[0] is not string instanceId || args[1] is not string controlId)
            return null;
        var toolkitId = _manager.GetToolkitId(instanceId);
        if (toolkitId is null)
            return null;
        _dataStore.Set(PanelScope.Key(toolkitId, instanceId, controlId, "value"), args[2]);
        return true;
    }

    private object? Dialog(object?[]? args)
    {
        // Dialog(instanceId, controlId, message, buttons...)
        if (args is null || args.Length < 3 || args[0] is not string instanceId || args[1] is not string controlId)
            return null;
        var toolkitId = _manager.GetToolkitId(instanceId);
        if (toolkitId is null)
            return null;
        var buttons = args.Skip(3).OfType<string>().ToArray();
        _dataStore.Set(PanelScope.Key(toolkitId, instanceId, controlId, "request"),
            new { message = args[2], buttons });
        return true;
    }

    private object? OpenPanel(object?[]? args)
    {
        if (args is null || args.Length < 1 || args[0] is not string instanceId)
            return null;
        _panelRuntime.RequestPanelOpen(instanceId);
        return true;
    }
}
