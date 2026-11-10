using System.Text.Json;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Plugin.Events;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using KitX.ToolKit.Contract.Declaration;
using Serilog;

namespace KitX.ToolKit.Triggers;

/// <summary>
/// A plugin-event trigger (Bench RFC §4.2 <c>PluginEvent</c>). Reuses the existing
/// plugin-event routing: it subscribes to <see cref="IPluginServer.PluginMessageReceived"/>,
/// identifies <c>TriggerFired</c> commands (exactly as the archived
/// <c>KitX.WorkflowV6.Services.TriggerManager</c> does), matches by
/// <see cref="TriggerConfig.PluginName"/> + <see cref="TriggerConfig.TriggerName"/> and
/// raises <see cref="ITriggerSource.Fired"/> with a JSON payload. Unlike the old
/// per-workflow routing, this is one of several <see cref="ITriggerSource"/>s and its
/// payload flows into the unified parameter channel.
/// </summary>
internal sealed class PluginEventTrigger : TriggerSourceBase
{
    /// <summary>
    /// Fallback plugin display name used when a TriggerFired command's sending connection
    /// cannot be resolved back to a registered plugin.
    /// </summary>
    public const string FallbackPluginName = "Unknown";

    /// <summary>
    /// The tag key a plugin uses to carry its trigger name on a TriggerFired command.
    /// Corresponds to the hardcoded <c>"TriggerName"</c> literal used by
    /// <see cref="KitX.Contract.CSharp.TriggerHelper.FireTrigger"/> (KitX Standard), which has
    /// no public constant of its own — the two must keep the same value so name/wildcard
    /// matching interoperates.
    /// </summary>
    public const string TriggerNameTagKey = "TriggerName";

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IPluginServer _pluginServer;
    private readonly TriggerConfig _config;
    private readonly string _pluginName;
    private bool _subscribed;
    private IDisposable? _routerToken;

    public PluginEventTrigger(string id, IPluginServer pluginServer, TriggerConfig config)
        : base(id, TriggerType.PluginEvent)
    {
        _pluginServer = pluginServer ?? throw new ArgumentNullException(nameof(pluginServer));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // PluginName is nullable in the contract because the field is shared by every trigger
        // type, but it is not optional *for this type*: the compiler rejects a PluginEvent trigger
        // without it (trigger.plugin-event.plugin-missing), so a trigger object that reaches here
        // empty can only come from direct construction. Failing here turns that into one
        // exception with the trigger's id in it, instead of two different wrong behaviours
        // depending on whether an IPluginEventRouter happens to be registered — a null name throws
        // out of the router's Register, and without a router it matches nothing at all, silently,
        // forever.
        if (string.IsNullOrWhiteSpace(config.PluginName))
        {
            throw new ArgumentException(
                $"PluginEvent trigger '{id}' has no Config.PluginName, so it can never match a message.",
                nameof(config));
        }

        // Captured once so the compiler can see it is non-null at every use site. The field on
        // TriggerConfig stays nullable (it is shared with trigger types that do not use it), but
        // this instance has already refused to exist without one.
        _pluginName = config.PluginName;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Prefers the shared <see cref="IPluginEventRouter"/> when the provider resolves one (the
    /// F3 fast path: a single process-wide subscription with O(1) dispatch). When no router is
    /// available — e.g. a direct-construction test that only supplies an <see cref="IPluginServer"/> —
    /// it falls back to a private direct subscription (<see cref="OnPluginMessageReceived"/>),
    /// preserving the historical behavior exactly.
    /// </remarks>
    public override void Start(IServiceProvider services)
    {
        if (_subscribed)
            return;

        if (services?.GetService(typeof(IPluginEventRouter)) is IPluginEventRouter router)
        {
            _routerToken = router.Register(_pluginName, _config.TriggerName, Raise);
            _subscribed = true;
            return;
        }

        _pluginServer.PluginMessageReceived += OnPluginMessageReceived;
        _subscribed = true;
    }

    /// <inheritdoc/>
    public override void Stop()
    {
        if (!_subscribed)
            return;

        if (_routerToken is not null)
        {
            _routerToken.Dispose();
            _routerToken = null;
        }
        else
        {
            _pluginServer.PluginMessageReceived -= OnPluginMessageReceived;
        }

        _subscribed = false;
    }

    private void OnPluginMessageReceived(object? sender, PluginMessageReceivedEventArgs e)
    {
        try
        {
            if (e.Message is null || e.ConnectionId is null)
                return;

            var kwc = JsonSerializer.Deserialize<Request>(e.Message, _serializerOptions);
            if (kwc?.Content is null)
                return;

            var command = JsonSerializer.Deserialize<Command>(kwc.Content, _serializerOptions);
            if (command.Request != CommandRequestInfo.TriggerFired)
                return;

            // Resolve the sending plugin name from the connection id.
            var connection = _pluginServer.Connections
                .FirstOrDefault(c => c.ConnectionId == e.ConnectionId);
            var pluginName = connection?.PluginInfo?.Name ?? FallbackPluginName;

            // Wildcard match: a null TriggerName matches any trigger of the plugin.
            var triggerName = command.Tags?.TryGetValue(TriggerNameTagKey, out var name) == true
                ? name : null;

            if (!string.Equals(pluginName, _pluginName, StringComparison.OrdinalIgnoreCase))
                return;
            if (!string.IsNullOrEmpty(_config.TriggerName) &&
                !string.Equals(triggerName, _config.TriggerName, StringComparison.OrdinalIgnoreCase))
                return;

            Log.Information("[PluginEventTrigger] Trigger '{Trigger}' fired by plugin '{Plugin}'",
                triggerName, pluginName);

            var payload = JsonSerializer.SerializeToElement(new
            {
                plugin = pluginName,
                trigger = triggerName,
                tags = command.Tags,
            });
            Raise(payload);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginEventTrigger] Error processing trigger message");
        }
    }
}
