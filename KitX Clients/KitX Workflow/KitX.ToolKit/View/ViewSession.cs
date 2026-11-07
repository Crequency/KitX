using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Storage;
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

        var loaded = _surfaces.Load(toolkitId);

        // A refused surface is reported as an empty card plus the reason on the server's log. It
        // does not fail the attach: the keys are still perfectly displayable, and refusing to
        // connect because a layout is broken would take the working half down with the other.
        if (loaded.Errors.Count > 0)
        {
            foreach (var error in loaded.Errors)
                Log.Warning("[ViewSession] {Code}: {Message}", error.Code, error.Message);
        }

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

    private void OnSurfaceChanged(object? sender, SurfaceChangedEventArgs e)
    {
        if (_disposed)
            return;

        var toolkitId = ToolkitId;
        if (toolkitId is null || !string.Equals(e.ToolkitId, toolkitId, StringComparison.Ordinal))
            return;

        // Whole document, never a diff (Surface §6.1): a layout has no per-element identity to diff
        // by, and inventing one to save bytes on a rarely-changed message is the wrong trade.
        Raise(new SurfaceMessage(_surfaces.Load(toolkitId).Document));
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
