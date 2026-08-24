using System.Collections.Concurrent;
using System.Text.Json;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using Serilog;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// Shared dispatch entry for all <see cref="PluginEventTrigger"/> sources. Instead of every
/// trigger subscribing to <see cref="IPluginServer.PluginMessageReceived"/> individually
/// (each subscription paying a full deserialize + a linear <c>Connections.FirstOrDefault</c>
/// scan per message), this router holds a <b>single</b> subscription and fans a message out
/// to every matching registration in O(1). The connection lookup uses
/// <see cref="IPluginServer.FindConnection"/> (already O(1)) rather than materializing the
/// full connection list.
///
/// <para>Semantics are identical to the pre-existing per-trigger path: a message is matched
/// when its deserialized command is <c>TriggerFired</c>, the sending plugin name equals the
/// registered plugin (OrdinalIgnoreCase) and, when a non-wildcard trigger name is registered,
/// the command's <c>TriggerName</c> tag matches it. A <see langword="null"/> trigger name is a
/// wildcard that matches any trigger of the plugin.</para>
/// </summary>
public interface IPluginEventRouter
{
    /// <summary>
    /// Registers a fire callback for the given plugin and trigger. Returns a disposable that,
    /// when disposed, removes the callback. A <see langword="null"/> <paramref name="triggerName"/>
    /// registers a wildcard that matches any trigger of the plugin. Multiple registrations for
    /// the same (plugin, trigger) pair are supported.
    /// </summary>
    /// <param name="pluginName">The plugin name to match (OrdinalIgnoreCase).</param>
    /// <param name="triggerName">The trigger name to match, or <see langword="null"/> for any trigger.</param>
    /// <param name="fire">The callback invoked with the JSON payload when a matching message arrives.</param>
    /// <returns>A disposable that unregisters the callback.</returns>
    IDisposable Register(string pluginName, string? triggerName, Action<JsonElement> fire);
}

/// <summary>
/// The default <see cref="IPluginEventRouter"/> implementation. See the interface remarks for
/// the routing contract.
///
/// <para><b>Subscription lifecycle.</b> The constructor holds only an <see cref="IServiceProvider"/>
/// and never directly depends on <see cref="IPluginServer"/> (a historical DI circular-dependency
/// lesson — the symptom of the cycle was a hung click, not a crash). <see cref="IPluginServer"/> is
/// resolved lazily from the provider and subscribed <b>once</b> on the first
/// <see cref="Register"/> call; the subscription is intentionally never torn down, so once all
/// registrations are disposed the router still owns a single subscription that returns after a
/// cheap <see cref="string.Contains(string,StringComparison)"/> pre-filter. Because the router is a
/// DI singleton, all <see cref="PluginEventTrigger"/> sources in the process share this one
/// subscription.</para>
///
/// <para><b>Matching index.</b> Registrations live in a single
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by a normalized
/// <c>(plugin-lower, trigger-lower-or-empty)</c> tuple. A <see langword="null"/>/empty trigger key
/// is the wildcard bucket. Lookup reads at most two buckets — the exact <c>(plugin, trigger)</c>
/// bucket and the wildcard <c>(plugin, "")</c> bucket — each an O(1) dictionary hit, and fans out to
/// every callback in the matching bucket(s). Callbacks are invoked synchronously on the IO callback
/// thread (the same fan-out behavior as the old direct subscription); one failing callback is
/// isolated so it cannot break routing for the others.</para>
/// </summary>
public sealed class PluginEventRouter : IPluginEventRouter
{
    private const string TriggerFiredLiteral = "TriggerFired";

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IServiceProvider _provider;
    private readonly object _subscribeGate = new();
    private readonly ConcurrentDictionary<(string Plugin, string Trigger), ConcurrentDictionary<long, Action<JsonElement>>>
        _registrations = new();

    private IPluginServer? _pluginServer;
    private long _nextId;
    private bool _subscribed;

    /// <summary>Creates a router that lazily resolves <see cref="IPluginServer"/> on first use.</summary>
    public PluginEventRouter(IServiceProvider provider)
        => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc/>
    public IDisposable Register(string pluginName, string? triggerName, Action<JsonElement> fire)
    {
        ArgumentNullException.ThrowIfNull(pluginName);
        ArgumentNullException.ThrowIfNull(fire);
        EnsureSubscribed();

        var key = NormalizeKey(pluginName, triggerName);
        var bucket = _registrations.GetOrAdd(key, _ => new ConcurrentDictionary<long, Action<JsonElement>>());
        var id = Interlocked.Increment(ref _nextId);
        bucket[id] = fire;

        return new Registration(this, key, id);
    }

    private static (string Plugin, string Trigger) NormalizeKey(string pluginName, string? triggerName)
        => (pluginName.Trim().ToLowerInvariant(), triggerName?.Trim().ToLowerInvariant() ?? string.Empty);

    private void EnsureSubscribed()
    {
        if (_subscribed)
            return;

        lock (_subscribeGate)
        {
            if (_subscribed)
                return;

            var server = _provider.GetService(typeof(IPluginServer)) as IPluginServer
                ?? throw new InvalidOperationException(
                    "IPluginServer is not registered in the service provider; cannot subscribe to plugin messages.");
            server.PluginMessageReceived += OnPluginMessageReceived;
            _pluginServer = server;
            _subscribed = true;
        }
    }

    private void OnPluginMessageReceived(object? sender, PluginMessageReceivedEventArgs e)
    {
        try
        {
            if (e.Message is null || e.ConnectionId is null)
                return;

            Command? command = e.Command;

            // Fallback path: when the raising source did not supply a parsed command, run the
            // original pre-filter + self-deserialize path. The pre-filter is a negative guard that
            // skips both deserializations for the overwhelming majority of messages that are not
            // TriggerFired commands (the literal's ASCII value is invariant under JSON encoding).
            // Messages that merely contain the word but are not the command are re-excluded by the
            // command.Request check below. When the source already parsed the command (e.Command is
            // present), the pre-filter and both deserializations are skipped entirely.
            if (command is null)
            {
                if (!e.Message.Contains(TriggerFiredLiteral, StringComparison.Ordinal))
                    return;

                var request = JsonSerializer.Deserialize<Request>(e.Message, _serializerOptions);
                if (request?.Content is null)
                    return;

                command = JsonSerializer.Deserialize<Command>(request.Content, _serializerOptions);
                if (command is null)
                    return;
            }

            if (command.Value.Request != CommandRequestInfo.TriggerFired)
                return;

            // O(1) connection lookup by id — no full-list materialization.
            var connection = _pluginServer?.FindConnection(e.ConnectionId);
            var pluginName = connection?.PluginInfo?.Name ?? PluginEventTrigger.FallbackPluginName;

            // Wildcard match: absent TriggerName tag means "any trigger of the plugin".
            var triggerName = command.Value.Tags?.TryGetValue(PluginEventTrigger.TriggerNameTagKey, out var name) == true
                ? name : null;

            var pluginKey = pluginName?.Trim().ToLowerInvariant()
                ?? PluginEventTrigger.FallbackPluginName.ToLowerInvariant();
            var triggerKey = triggerName?.Trim().ToLowerInvariant() ?? string.Empty;

            // Gather the fire callbacks from the exact bucket and the wildcard bucket. When the
            // command carries no trigger name (triggerKey is empty) the exact and wildcard buckets
            // are the same key, so only one lookup is needed.
            var fireAll = GatherFireCallbacks(pluginKey, triggerKey);
            if (fireAll.Count == 0)
                return;

            // Build the payload once and share it across every matching registration.
            var payload = JsonSerializer.SerializeToElement(new
            {
                plugin = pluginName,
                trigger = triggerName,
                tags = command.Value.Tags,
            });

            foreach (var fire in fireAll)
            {
                try
                {
                    fire(payload);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[PluginEventRouter] Error dispatching to a trigger registration");
                }
            }

            Log.Information("[PluginEventTrigger] Trigger '{Trigger}' fired by plugin '{Plugin}'",
                triggerName, pluginName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginEventRouter] Error processing trigger message");
        }
    }

    /// <summary>
    /// Collects the fire callbacks from the exact <c>(plugin, trigger)</c> bucket and the wildcard
    /// <c>(plugin, "")</c> bucket into a snapshot list, so the payload is built once and an empty
    /// match set avoids building it at all. When the command carries no trigger name the exact and
    /// wildcard buckets are the same key, so only one lookup is performed.
    /// </summary>
    private List<Action<JsonElement>> GatherFireCallbacks(string pluginKey, string triggerKey)
    {
        var callbacks = new List<Action<JsonElement>>();
        if (_registrations.TryGetValue((pluginKey, triggerKey), out var exact))
            callbacks.AddRange(exact.Values);
        if (triggerKey.Length > 0 &&
            _registrations.TryGetValue((pluginKey, string.Empty), out var wildcard))
            callbacks.AddRange(wildcard.Values);
        return callbacks;
    }

    /// <summary>
    /// Removes a single registration from its bucket on dispose. Idempotent. The bucket is
    /// pruned from the index once empty so a long-lived process does not accumulate dead keys
    /// (the router keeps its single server subscription regardless).
    /// </summary>
    private sealed class Registration : IDisposable
    {
        private readonly PluginEventRouter _owner;
        private readonly (string Plugin, string Trigger) _key;
        private readonly long _id;
        private int _disposed;

        public Registration(PluginEventRouter owner, (string Plugin, string Trigger) key, long id)
        {
            _owner = owner;
            _key = key;
            _id = id;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (_owner._registrations.TryGetValue(_key, out var bucket))
            {
                bucket.TryRemove(_id, out _);
                if (bucket.IsEmpty)
                    _owner._registrations.TryRemove(_key, out _);
            }
        }
    }
}
