using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Time;
using Serilog;

namespace KitX.ToolKit.View;

/// <summary>
/// The server end of the View protocol: one session per connection, commands mapped onto the
/// management facade.
/// </summary>
/// <remarks>
/// <para><b>A transport-agnostic channel, not a transport.</b> It owns no socket. A host that wants
/// WebSockets constructs one of these per connection and pumps messages through
/// <see cref="SendAsync"/>; the pump is P4's problem, and keeping it out of here is what makes the
/// command mapping testable without a network.</para>
///
/// <para><b>Every command corresponds to a member on the management side</b> (ADR-0014 D1). When one
/// does not, the semantic-unit table is missing a row — not "the network needs something extra".
/// The three clear commands are the documented exception: they are the only operations that exist
/// on the wire and nowhere else, because clearing a key is something a workflow does, and a View is
/// the only other actor that legitimately does it.</para>
///
/// <para><b>Refusals are replies, not exceptions.</b> <see cref="SendAsync"/> catches
/// <see cref="ViewProtocolException"/> and returns <see cref="ChannelReply.Rejected"/>. Only
/// cancellation escapes, and only because the caller changing its mind is not the server refusing.
/// </para>
/// </remarks>
internal sealed class ViewChannel : IViewChannel, IDisposable
{
    private readonly ToolkitInstanceManager _manager;
    private readonly ToolkitStore _toolkits;
    private readonly ViewSession _session;
    private readonly IIdGenerator _ids;

    private bool _disposed;

    public ViewChannel(
        ToolkitInstanceManager manager,
        ToolkitStore toolkits,
        DataStore store,
        ISurfaceStore surfaces,
        IIdGenerator ids)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _toolkits = toolkits ?? throw new ArgumentNullException(nameof(toolkits));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));

        _session = new ViewSession(ids.NewFlowId(), manager, store, surfaces);
        _session.Received += (_, message) => Received?.Invoke(this, message);
    }

    /// <inheritdoc/>
    public event EventHandler<ViewMessage>? Received;

    /// <summary>The ToolKit this channel is attached to, or <see langword="null"/>.</summary>
    internal string? ToolkitId => _session.ToolkitId;

    /// <inheritdoc/>
    public ValueTask<ChannelReply> SendAsync(
        ViewMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (_disposed)
        {
            return ValueTask.FromResult(ChannelReply.Rejected(
                ViewErrorCodes.Failed, "The channel has been closed."));
        }

        try
        {
            return ValueTask.FromResult(Dispatch(message));
        }
        catch (ViewProtocolException ex)
        {
            Log.Information("[ViewChannel] Refused {Type}: {Code} {Message}",
                message.GetType().Name, ex.Code, ex.Message);
            return ValueTask.FromResult(ChannelReply.Rejected(ex.Code, ex.Message));
        }
    }

    private ChannelReply Dispatch(ViewMessage message) => message switch
    {
        ConnectMessage connect => HandleConnect(connect),
        CommandMessage command => HandleCommand(command),
        ListMessage => ChannelReply.Accepted(new ToolkitsMessage(ListToolkits())),

        // Push-only messages arriving from a client are a protocol error, but not one worth
        // dropping the connection over: the client may be echoing something it misread.
        DeltaMessage or SurfaceMessage or ClearedMessage =>
            throw new ViewProtocolException(
                ViewErrorCodes.UnknownCommand,
                $"'{message.GetType().Name}' is server-to-client only."),

        AckedReply or RejectedReply or WelcomeMessage or ToolkitsMessage =>
            throw new ViewProtocolException(
                ViewErrorCodes.UnknownCommand,
                $"'{message.GetType().Name}' is a server reply and cannot be sent to a server."),

        _ => throw new ViewProtocolException(
            ViewErrorCodes.Malformed, $"Unrecognised message type '{message.GetType().Name}'."),
    };

    /// <summary>
    /// Attaches, after checking the protocol version. A version this build does not speak is refused
    /// rather than downgraded: a silent downgrade renders with rules the server never agreed to, and
    /// the mismatch surfaces as a wrong value instead of a failed connection (§5.9.10).
    /// </summary>
    private ChannelReply HandleConnect(ConnectMessage connect)
    {
        if (connect.ProtocolVersion != ConnectMessage.CurrentProtocolVersion)
        {
            throw new ViewProtocolException(
                ViewErrorCodes.Protocol,
                $"This build speaks protocol version {ConnectMessage.CurrentProtocolVersion}; " +
                $"the client sent {connect.ProtocolVersion}.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(connect.ToolkitId);

        var welcome = _session.Attach(connect.ToolkitId);
        return ChannelReply.Accepted(welcome);
    }

    /// <summary>
    /// Runs one command against the management facade.
    /// </summary>
    /// <remarks>
    /// Every branch resolves its arguments before acting, so a command that cannot be carried out
    /// leaves no half-effect. That matters most for the two-value commands
    /// (<c>trigger</c>, <c>event</c>): a missing argument discovered after the flow started would
    /// mean a started flow and a refusal in the same reply.
    /// </remarks>
    private ChannelReply HandleCommand(CommandMessage command)
    {
        var toolkitId = _session.ToolkitId
            ?? throw new ViewProtocolException(
                ViewErrorCodes.NotAttached,
                "Send 'connect' before any command.");

        var name = command.Command ?? string.Empty;

        return name switch
        {
            ViewCommands.Trigger => Trigger(toolkitId, command),
            ViewCommands.Event => RaiseEvent(toolkitId, command),

            ViewCommands.Remove => Remove(toolkitId, command),
            ViewCommands.ClearRegion => ClearRegion(toolkitId, command),
            ViewCommands.ClearAll => ClearAll(toolkitId),

            ViewCommands.Enable => Lifecycle(toolkitId, enable: true),
            ViewCommands.Disable => Lifecycle(toolkitId, enable: false),

            ViewCommands.Mount => Mount(toolkitId),
            ViewCommands.Unmount => Unmount(toolkitId),

            _ => throw new ViewProtocolException(
                ViewErrorCodes.UnknownCommand, $"Unknown command '{name}'."),
        };
    }

    private ChannelReply Trigger(string toolkitId, CommandMessage command)
    {
        var triggerId = Require(command.TriggerId, nameof(CommandMessage.TriggerId));

        var flowId = _manager.Spawn(toolkitId, triggerId)
            ?? throw new ViewProtocolException(
                ViewErrorCodes.Failed,
                $"Trigger '{triggerId}' did not start a flow — it may be disabled, unknown, or " +
                "refused by the concurrency limit.");

        return ChannelReply.Accepted(new AckedReply
        {
            Command = ViewCommands.Trigger,
            Values = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["flowId"] = System.Text.Json.JsonSerializer.SerializeToElement(flowId),
            },
        });
    }

    private ChannelReply RaiseEvent(string toolkitId, CommandMessage command)
    {
        var eventName = Require(command.EventName, nameof(CommandMessage.EventName));

        var started = _manager.RaiseEvent(toolkitId, eventName);
        return ChannelReply.Accepted(new AckedReply
        {
            Command = ViewCommands.Event,
            Values = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["started"] = System.Text.Json.JsonSerializer.SerializeToElement(started),
            },
        });
    }

    private ChannelReply Remove(string toolkitId, CommandMessage command)
    {
        var key = Require(command.Key, nameof(CommandMessage.Key));

        // A key the ToolKit does not declare is refused rather than silently ignored: the client
        // would otherwise believe it removed something, and the next delta would not correct it.
        if (_manager.GetKeyTableFor(toolkitId) is not { } table || !table.IsDeclared(key))
        {
            throw new ViewProtocolException(
                ViewErrorCodes.MissingArgument, $"ToolKit '{toolkitId}' declares no key '{key}'.");
        }

        return ChannelReply.Accepted(new AckedReply
        {
            Command = ViewCommands.Remove,
            Values = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["removed"] = System.Text.Json.JsonSerializer.SerializeToElement(
                    _manager.RemoveLocal(toolkitId, key)),
            },
        });
    }

    private ChannelReply ClearRegion(string toolkitId, CommandMessage command)
    {
        var region = Require(command.Region, nameof(CommandMessage.Region));

        var removed = _manager.ClearRegionOf(toolkitId, region);
        return ChannelReply.Accepted(new AckedReply
        {
            Command = ViewCommands.ClearRegion,
            Values = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["removed"] = System.Text.Json.JsonSerializer.SerializeToElement(removed),
            },
        });
    }

    /// <summary>
    /// Clears every declared value. The client learns of it through the single
    /// <c>cleared</c> message the store's own event produces, not through this reply.
    /// </summary>
    private ChannelReply ClearAll(string toolkitId)
    {
        var removed = _manager.ClearAllOf(toolkitId);
        return ChannelReply.Accepted(new AckedReply
        {
            Command = ViewCommands.ClearAll,
            Values = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["removed"] = System.Text.Json.JsonSerializer.SerializeToElement(removed),
            },
        });
    }

    private ChannelReply Lifecycle(string toolkitId, bool enable)
    {
        if (!_manager.IsMounted(toolkitId))
        {
            throw new ViewProtocolException(
                ViewErrorCodes.Failed,
                $"ToolKit '{toolkitId}' is not mounted, so it has no lifecycle to change.");
        }

        if (enable)
            _manager.Enable(toolkitId);
        else
            _manager.Disable(toolkitId);

        return ChannelReply.Accepted(new AckedReply
        {
            Command = enable ? ViewCommands.Enable : ViewCommands.Disable,
        });
    }

    /// <summary>
    /// Mounts the attached ToolKit.
    /// </summary>
    /// <remarks>
    /// Refused when the session is not attached: <c>mount</c> names no id, so it can only act on the
    /// one the session already holds. That is why <c>connect</c> does not mount — attaching to a
    /// ToolKit and starting its triggers are separate decisions, and a View that only reads should
    /// be able to attach without arming anything.
    /// </remarks>
    private ChannelReply Mount(string toolkitId)
    {
        if (_manager.IsMounted(toolkitId))
            return ChannelReply.Accepted(new AckedReply { Command = ViewCommands.Mount });

        var toolkit = _toolkits.Load(toolkitId)
            ?? throw new ViewProtocolException(
                ViewErrorCodes.UnknownToolkit, $"ToolKit '{toolkitId}' could not be loaded.");

        _manager.Mount(toolkit);
        return ChannelReply.Accepted(new AckedReply { Command = ViewCommands.Mount });
    }

    /// <summary>
    /// Unmounts the attached ToolKit. Its declared values survive (ADR-0005 D2): unmounting forgets
    /// the scheduler, it does not throw data away. Removing the data is <c>uninstall</c>, which is
    /// management-only and therefore absent from this protocol.
    /// </summary>
    private ChannelReply Unmount(string toolkitId)
    {
        _manager.Unmount(toolkitId);
        return ChannelReply.Accepted(new AckedReply { Command = ViewCommands.Unmount });
    }

    private IReadOnlyList<ToolkitSummary> ListToolkits()
    {
        var mounted = new HashSet<string>(_manager.MountedToolkitIds, StringComparer.Ordinal);

        return _toolkits.List()
            .Select(t =>
            {
                var id = t.GetId() ?? string.Empty;
                return new ToolkitSummary(
                    id,
                    t.Meta?.Name ?? id,
                    _manager.GetLifecycleState(id) ?? LifecycleState.Installed,
                    mounted.Contains(id));
            })
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static string Require(string? value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ViewProtocolException(
                ViewErrorCodes.MissingArgument, $"The command needs '{argumentName}'.");
        }

        return value;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _session.Dispose();
    }
}
