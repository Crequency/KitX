using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Surface;
using Serilog;

namespace KitX.ToolKit.View;

/// <summary>
/// One connected View: what it is attached to, and how server-side changes become messages.
/// </summary>
/// <remarks>
/// <para><b>A session is a projection, not a copy of authority.</b> It holds no values. When it needs
/// the current state it asks the manager and the store; when they change it forwards the change.
/// Keeping a cache here would create a place where the "current value" disagrees with the store, and
/// the disagreement would be invisible until someone rendered from the wrong one.</para>
///
/// <para><b>One session, one ToolKit.</b> Switching means <see cref="Attach"/>, which drops the old
/// subscriptions first. A second connection does not get a second window onto the same session —
/// the session id is the identity, and re-attaching replaces what it is subscribed to.</para>
///
/// <para><b>Subscribes to the store, not to the manager's event stream.</b> The manager raises
/// flow-level events (<see cref="FlowCompletedEvent"/>, <see cref="TriggerRejectedEvent"/>); a View
/// shows keys, and keys change through <see cref="DataStore"/>. Going through the manager would mean
/// the View only saw changes some other component happened to narrate.</para>
/// </remarks>
internal sealed class ViewSession : IDisposable
{
    private readonly ToolkitInstanceManager _manager;
    private readonly DataStore _store;
    private readonly ISurfaceStore _surfaces;

    /// <summary>Held so a detached session cannot receive from a subscription it did not cancel.</summary>
    private readonly object _gate = new();

    private string? _toolkitId;
    private bool _disposed;

    public ViewSession(
        string sessionId,
        ToolkitInstanceManager manager,
        DataStore store,
        ISurfaceStore surfaces)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));

        _store.Changed += OnStoreChanged;
        _store.Cleared += OnStoreCleared;
        _store.RemovedMany += OnStoreRemovedMany;
        _surfaces.Changed += OnSurfaceChanged;
    }

    /// <summary>This session's identity. Stable across re-attach.</summary>
    public string SessionId { get; }

    /// <summary>The ToolKit this session is attached to, or <see langword="null"/>.</summary>
    public string? ToolkitId
    {
        get { lock (_gate) return _toolkitId; }
    }

    /// <summary>Raised for messages the server pushes without being asked.</summary>
    public event EventHandler<ViewMessage>? Received;

    /// <summary>Who the client said it was at connect time, or null if it said nothing.</summary>
    /// <remarks>
    /// Set once by <see cref="SetDevice"/>, before any attach. It is not per-attachment: a session
    /// represents one connected client, and that client does not become a different device by
    /// attaching to a different ToolKit.
    /// <para>
    /// It is a report, not a credential (Q26 removed authentication here). Treat it as a hint.
    /// </para>
    /// </remarks>
    public Initiator Device { get; private set; } = Initiator.Unknown;

    /// <summary>Records the device the client claims to be. Called once, at connect.</summary>
    /// <remarks>
    /// Both fields are optional and independent: a client may know its id and not a display name,
    /// or the reverse. <see cref="Initiator.Unknown"/> is what "said nothing" looks like — its
    /// <see cref="Initiator.IsUnknown"/> check reads the empty id, so a client that names itself
    /// but has no device id is still unknown, which is the honest answer: a display name is not
    /// an identity.
    /// </remarks>
    public void SetDevice(string? deviceId, string? deviceName)
    {
        var id = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : deviceId;
        var name = string.IsNullOrWhiteSpace(deviceName) ? string.Empty : deviceName;

        Device = new Initiator(id, name);
    }

    /// <summary>
    /// Attaches to a ToolKit, replacing any previous attachment, and returns the welcome message.
    /// </summary>
    /// <remarks>
    /// The subscriptions are session-wide (created once, in the constructor) rather than per
    /// attachment, and they filter on <see cref="ToolkitId"/>. Per-attachment subscriptions would
    /// have to be torn down on every re-attach, and a missed removal would let a session keep
    /// receiving from the ToolKit it just left.
    /// </remarks>
    public WelcomeMessage Attach(string toolkitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitId);

        if (!_manager.IsMounted(toolkitId))
            throw new ViewProtocolException(
                ViewErrorCodes.UnknownToolkit,
                $"ToolKit '{toolkitId}' is not mounted, so it has no live state to attach to.");

        lock (_gate)
            _toolkitId = toolkitId;

        var loaded = Normalise(_surfaces.Load(toolkitId), toolkitId);

        // A refused surface is reported as an empty card plus the reason on the server's log. It
        // does not fail the attach: the keys are still perfectly displayable, and refusing to
        // connect because a layout is broken would take the working half down with the other.
        //
        // Warnings get the same treatment, and they need it just as much. A warning means one
        // element was dropped and the rest loaded — so the card is *missing a row*, which is far
        // easier to blame on the renderer than on a typo in the file. Without a log line the only
        // observable is "the card looks short", and nothing connects that to a mistyped `type`
        // discriminator three layers down. `SurfaceLoadResult` already distinguishes the two lists
        // so callers can act differently; here they act the same way, because a server has exactly
        // one place to say anything at all.
        foreach (var diagnostic in loaded.Errors.Concat(loaded.Warnings))
            Log.Warning("[ViewSession] {Code}: {Message}", diagnostic.Code, diagnostic.Message);

        return new WelcomeMessage(toolkitId, _manager.LocalValues(toolkitId), loaded.Document);
    }

    /// <summary>Forwarded to the wire when the client subscribes to a key's changes.</summary>
    private void OnStoreChanged(object? sender, DataStoreChangedEventArgs e)
    {
        var toolkitId = ToolkitId;
        if (toolkitId is null || _disposed)
            return;

        // The store is process-wide: only forward keys this session's ToolKit owns. Comparing the
        // local name against the declared table is what scopes the message, and it is the same
        // check the data plane uses — a second, looser one here would be a second source of truth
        // about what belongs to whom.
        if (!TrySplit(e.Key, toolkitId, out var local))
            return;

        var table = _manager.GetKeyTableFor(toolkitId);
        if (table is null || !table.IsDeclared(local))
            return;

        var message = e.Removed
            ? new DeltaMessage(local, null)
            : new DeltaMessage(local, e.NewValue) { Appended = e.Appended };

        Raise(message);
    }

    /// <summary>
    /// One <c>cleared</c> per clear, never N removals (design completion criterion 4).
    /// </summary>
    /// <remarks>
    /// The store raises a single <c>Cleared</c> because it genuinely does not know which keys a
    /// caller cared about; expanding it into per-key deltas would require enumerating the table on
    /// every clear and would leave the client holding a half-cleared state in between.
    /// </remarks>
    private void OnStoreCleared(object? sender, EventArgs e)
    {
        var toolkitId = ToolkitId;
        if (toolkitId is null || _disposed)
            return;

        Raise(new ClearedMessage(toolkitId));
    }

    /// <summary>
    /// A batch deletion finished. Projects it as one <c>cleared</c>, but only when it touched this
    /// session's ToolKit.
    /// </summary>
    /// <remarks>
    /// <para>The message carries no key names, and that is safe for a structural reason worth
    /// writing down: everything in a client's projection arrived through a <c>welcome</c> or a
    /// <c>delta</c>, and both were filtered to this ToolKit's declared keys on the way out. So
    /// "clear everything I have" and "clear this ToolKit's keys" describe the same set — not because
    /// they happen to coincide today, but because the projection can hold nothing else.</para>
    ///
    /// <para>This is separate from <see cref="OnStoreCleared"/> because the two events mean
    /// different things. <c>Cleared</c> says the whole store is empty; this one says a subset went
    /// away, and a subscriber that treated it as a wipe would clear a client that is attached to a
    /// different ToolKit.</para>
    /// </remarks>
    private void OnStoreRemovedMany(object? sender, DataStoreRemovedManyEventArgs e)
    {
        var toolkitId = ToolkitId;
        if (toolkitId is null || _disposed)
            return;

        var prefix = toolkitId + "/";
        var touched = false;
        foreach (var key in e.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                touched = true;
                break;
            }
        }

        if (!touched)
            return;

        Raise(new ClearedMessage(toolkitId));
    }

    private void OnSurfaceChanged(object? sender, SurfaceChangedEventArgs e)
    {
        if (_disposed)
            return;

        var toolkitId = ToolkitId;
        if (toolkitId is null || !string.Equals(e.ToolkitId, toolkitId, StringComparison.Ordinal))
            return;

        // Whole document, never a diff (Surface §6.1): a layout has no per-element identity to diff
        // by, and inventing one to save bytes on a rarely-changed message is the wrong trade.
        Raise(new SurfaceMessage(Normalise(_surfaces.Load(toolkitId), toolkitId).Document));
    }

    /// <summary>
    /// Runs the surface validator over a loaded document so a View receives what a renderer can draw.
    /// </summary>
    /// <remarks>
    /// <para><b>The server side of a ruling that was written down and never implemented.</b>
    /// <c>Package/ToolKit-设计-03-Surface.md</c> §3.3.7 (<c>:808-815</c>) says validation happens
    /// "on the server at connect, and again when the orchestrator saves, written the same way". The
    /// saving half has always run; this is the connect half, and it exists because a View is not a
    /// validating reader.</para>
    ///
    /// <para><b>Why the renderer cannot do this instead.</b> <c>SurfaceCard</c> registers a cell by
    /// the presence of a <c>key</c> and then shows it by kind (<c>:629-630</c>, <c>:731-734</c>).
    /// An <c>Input</c> that declares a key therefore gets registered and then has no way to be shown —
    /// which is exactly what a remote client met: <c>no way to show a value in TextBox</c>. The
    /// validator's job is to strip that key into a warning (<c>SurfaceValidator.cs:341-348</c>), and
    /// the renderer's own remarks say it does not run that step.</para>
    ///
    /// <para><b>Both messages go through here, and that is the point.</b> Normalising only the
    /// <c>welcome</c> would make the card correct until the first <c>surface</c> push, and then wrong
    /// again — a defect that shows up minutes into a session rather than at connect.</para>
    ///
    /// <para><b>An unmounted ToolKit is sent as-is, not as an empty card.</b> There is no key table
    /// without a mount, and validating against an empty one would call every declared key unknown and
    /// drop the elements that use it — turning "not mounted yet" into a blank card nobody can explain.
    /// A non-conforming document is the lesser wrong, and this is the one place where that trade is
    /// deliberate rather than accidental.</para>
    /// </remarks>
    private SurfaceLoadResult Normalise(SurfaceLoadResult loaded, string toolkitId)
    {
        if (loaded.Errors.Count > 0 || _manager.GetKeyTableFor(toolkitId) is not { } keys)
            return loaded;

        var validated = SurfaceValidator.Validate(loaded.Document, keys);

        if (validated.Warnings.Count == 0)
            return new SurfaceLoadResult(validated.Effective, loaded.Errors, loaded.Warnings);

        return new SurfaceLoadResult(
            validated.Effective,
            loaded.Errors,
            [.. loaded.Warnings, .. validated.Warnings]);
    }

    private void Raise(ViewMessage message)
    {
        // Outside the lock: a subscriber is allowed to call back into this session, and holding
        // _gate across an arbitrary callback is how a deadlock is discovered in production.
        Received?.Invoke(this, message);
    }

    /// <summary>
    /// Whether a fully-qualified store key belongs to <paramref name="toolkitId"/>, yielding its
    /// local name.
    /// </summary>
    private static bool TrySplit(string key, string toolkitId, out string local)
    {
        var prefix = toolkitId + "/";
        if (key.StartsWith(prefix, StringComparison.Ordinal))
        {
            local = key[prefix.Length..];
            return local.Length > 0 && !local.Contains('/');
        }

        local = string.Empty;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _store.Changed -= OnStoreChanged;
        _store.Cleared -= OnStoreCleared;
        _store.RemovedMany -= OnStoreRemovedMany;
        _surfaces.Changed -= OnSurfaceChanged;

        lock (_gate)
            _toolkitId = null;
    }
}

/// <summary>
/// A protocol-level failure the channel turns into a <c>rejected</c> reply.
/// </summary>
/// <remarks>
/// <b>Internal, and it never crosses <see cref="IViewChannel"/>.</b> The channel catches it and
/// returns <see cref="ChannelReply.Rejected"/>; if it ever escaped, the protocol would be expressing
/// failure as an exception, which §2 forbids. It exists because the alternative — every method
/// returning a two-shape result through every internal call — buries the happy path in branches.
/// </remarks>
internal sealed class ViewProtocolException : Exception
{
    public ViewProtocolException(string code, string message) : base(message) => Code = code;

    /// <summary>One of <see cref="ViewErrorCodes"/>.</summary>
    public string Code { get; }
}
