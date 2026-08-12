using System.Text.Json;
using System.Text.Json.Nodes;

namespace KitX.ToolKit.Data;

/// <summary>
/// Exposes the <see cref="DataStore"/> to workflows as a built-in plugin (the user-chosen
/// integration: DataStore lives "as a built-in plugin" from the workflow's perspective).
///
/// <para>A workflow calls it exactly like a plugin function — e.g.
/// <c>PluginCall("KitX.DataStore", "Set", "status", "running")</c> — reusing the existing
/// <c>PluginCall</c> path. The host's <c>IPluginHost</c> implementation intercepts the
/// reserved plugin name <see cref="PluginName"/> and routes here, so <b>KitX.WorkflowV6
/// itself is untouched</b> (the functions are ordinary plugin calls).</para>
///
/// <para>Function family (Bench RFC §6.2): Set / Get / Wait (AND) / WaitAny (OR) /
/// Remove / Keys / Contains.</para>
/// </summary>
public sealed class BuiltinDataStorePlugin
{
    /// <summary>The reserved plugin name a workflow uses to reach the DataStore.</summary>
    public const string PluginName = "KitX.DataStore";

    private readonly DataStore _dataStore;
    private readonly DataStoreOptions _options;

    public BuiltinDataStorePlugin(DataStore dataStore, DataStoreOptions? options = null)
    {
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _options = options ?? new DataStoreOptions();
    }

    /// <summary>
    /// Dispatches a plugin function call onto the DataStore. Method names are
    /// case-insensitive; unknown methods return null (matching the safe-default
    /// convention used elsewhere in the host).
    /// </summary>
    public object? Invoke(string methodName, object?[]? args)
    {
        var name = methodName?.ToLowerInvariant();
        return name switch
        {
            "set" => Set(args),
            "get" => Get(args),
            "wait" => Wait(args, any: false),
            "waitany" => Wait(args, any: true),
            "remove" => Remove(args),
            "keys" => Keys(),
            "contains" => Contains(args),
            _ => null,
        };
    }

    /// <summary>Returns true when this plugin provides <paramref name="methodName"/>.</summary>
    public bool HasMethod(string methodName)
        => methodName?.ToLowerInvariant() is "set" or "get" or "wait" or "waitany" or "remove" or "keys" or "contains";

    private object? Set(object?[]? args)
    {
        if (args is null || args.Length < 2 || args[0] is not string key)
            return null;
        _dataStore.Set(key, args[1]);
        return true;
    }

    private object? Get(object?[]? args)
    {
        if (args is null || args.Length < 1 || args[0] is not string key)
            return null;
        return _dataStore.Get(key);
    }

    private object? Wait(object?[]? args, bool any)
    {
        var keys = (args ?? []).OfType<string>().ToArray();
        if (keys.Length == 0)
            return new JsonObject();
        return any
            ? _dataStore.WaitAny(keys, _options.DefaultWaitTimeout)
            : _dataStore.Wait(keys, _options.DefaultWaitTimeout);
    }

    private object? Remove(object?[]? args)
    {
        if (args is null || args.Length < 1 || args[0] is not string key)
            return null;
        return _dataStore.Remove(key);
    }

    private object? Keys()
        => JsonSerializer.SerializeToElement(_dataStore.Keys().ToArray());

    private object? Contains(object?[]? args)
    {
        if (args is null || args.Length < 1 || args[0] is not string key)
            return null;
        return _dataStore.Contains(key);
    }
}
