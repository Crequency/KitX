namespace KitX.Core.Plugin;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kscript.CSharp.Parser.Core;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Shared.CSharp.Plugin;

// ─────────────────────────────────────────────────────────────────────────────
// DashboardPluginServiceProvider — bridges the host's live plugin services
// (IPluginServer + IEventService) to the Kscript IPluginServiceProvider contract.
//
// This is the missing implementation behind §2.3 (Dashboard-Frontend-Refactor-
// Handoff.md §二.3). Without it, PluginHostAdapter falls back to
// NoOpPluginManager and every workflow PluginCall(...) returns null at runtime.
//
// Both sides share the same KitX.Shared.CSharp.Plugin.PluginInfo type, so no
// conversion is needed. The bridge logic mirrors PluginsManager.CallPluginFunctionAsync
// (KitX.Core/Plugin/PluginsManager.cs), which is the host's native plugin-call
// path — the lookup-by-name, connector request, and response correlation all line up.
//
// Registered as a singleton in CoreServiceCollectionExtensions alongside RealPluginManager.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Adapts the host's <see cref="IPluginServer"/> and <see cref="IEventService"/>
/// to Kscript's <see cref="IPluginServiceProvider"/>. This wires up
/// <see cref="RealPluginManager"/> so workflow <c>PluginCall</c> builtins reach live plugins.
/// </summary>
public sealed class DashboardPluginServiceProvider : IPluginServiceProvider
{
    private readonly IPluginServer _pluginServer;
    private readonly IEventService _eventService;

    public DashboardPluginServiceProvider(IPluginServer pluginServer, IEventService eventService)
    {
        _pluginServer = pluginServer ?? throw new ArgumentNullException(nameof(pluginServer));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
    }

    /// <inheritdoc/>
    public IEnumerable<PluginInfo> GetRunningPlugins()
    {
        foreach (var conn in _pluginServer.Connections)
        {
            if (conn.PluginInfo is { } info)
                yield return info;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Looks up by <see cref="PluginInfo.Name"/> over <see cref="IPluginServer.Connections"/>
    /// (the interface has no FindConnection-by-info overload; the concrete server's
    /// <c>FindConnection(PluginInfo)</c> also matches by Name since C-9).
    /// </remarks>
    public PluginInfo? FindPlugin(string pluginName)
    {
        if (string.IsNullOrEmpty(pluginName))
            return null;

        foreach (var conn in _pluginServer.Connections)
        {
            if (conn.PluginInfo?.Name == pluginName)
                return conn.PluginInfo;
        }
        return null;
    }

    /// <inheritdoc/>
    /// <returns>The <see cref="IPluginConnection"/> boxed as <see cref="object"/>, looked up by name.</returns>
    public object? FindConnector(PluginInfo pluginInfo)
    {
        return _pluginServer.FindConnector(pluginInfo);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Casts the connector (produced by <see cref="FindConnector"/>) back to
    /// <see cref="IPluginConnector"/> and delegates to <see cref="IPluginConnector.Request"/>,
    /// which serializes and sends the request over the plugin's WebSocket.
    /// </remarks>
    public Task SendRequestAsync(object connector, object request)
    {
        if (connector is IPluginConnector pc)
        {
            pc.Request(request);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Subscribes to the host's <c>PluginResponse</c> event channel and forwards
    /// <c>(RequestId, Content)</c> to the handler. <c>Content</c> is the serialized Command
    /// JSON, which is exactly what <see cref="RealPluginManager.HandlePluginResponse"/> expects.
    /// </remarks>
    public void SubscribeToResponses(Action<string, string> responseHandler)
    {
        _eventService.Subscribe<PluginResponseEventArgs>(
            EventNames.PluginResponse,
            (_, e) => responseHandler(e.RequestId, e.Content));
    }
}
