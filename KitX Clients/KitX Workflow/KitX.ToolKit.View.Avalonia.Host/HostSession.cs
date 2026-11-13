using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Hosting;
using KitX.ToolKit.View;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// Everything the window does, minus the window: compose, mount, list, connect, apply.
/// </summary>
/// <remarks>
/// <para><b>Split out so the chain can be tested without a desktop.</b> If all of this lived in
/// <c>HostWindow</c>, the only way to check that a <c>delta</c> reaches a <see cref="SurfaceCard"/>
/// would be to open a window — which a test cannot do reliably and a headless runner cannot do at
/// all. The window becomes a thin view over this, and the assertions land here.</para>
///
/// <para><b>It touches only contract types.</b> <see cref="IToolkitService"/>, <see cref="ISurfaceStore"/>,
/// <see cref="IViewChannel"/>, <see cref="ToolkitView"/> and <see cref="SurfaceCard"/> are all on the
/// public side. The implementation assembly (<c>KitX.ToolKit</c>) is reached through the DI
/// container and never named — see <c>ProductBoundaryTests</c>, which asserts the compiled metadata
/// of this assembly carries no reference to it.</para>
///
/// <para><b>Order is load-bearing: mount, then list.</b> Since 2026-11-11 a View sees only mounted
/// ToolKits, so <c>list</c> on a fresh process returns nothing at all. <c>mount</c> is not a wire
/// command — it was removed for the same reason — so the host calls
/// <see cref="IToolkitService.Mount"/> itself. Doing it the other way round yields an empty list
/// and no error, which is the shape of a bug that takes an afternoon to find.</para>
/// </remarks>
public sealed class HostSession : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly Func<IViewChannel> _newChannel;

    private IViewChannel? _channel;
    private bool _disposed;

    /// <summary>Composes the server graph and prepares the sample ToolKit root.</summary>
    /// <param name="toolkitRoot">Where this session's ToolKits live.</param>
    public HostSession(string toolkitRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitRoot);

        Root = toolkitRoot;

        _provider = new ServiceCollection()
            .AddKitXWorkflowV6()
            .AddKitXToolKit(toolkitRoot)
            .BuildServiceProvider();

        Toolkits = _provider.GetRequiredService<IToolkitService>();
        Surfaces = _provider.GetRequiredService<ISurfaceStore>();
        WorkflowFiles = _provider.GetRequiredService<IToolkitWorkflowFileStore>();
        _newChannel = _provider.GetRequiredService<Func<IViewChannel>>();
    }

    /// <summary>The storage root this session owns.</summary>
    public string Root { get; }

    /// <summary>The management facade — used to mount, and for nothing else.</summary>
    public IToolkitService Toolkits { get; }

    /// <summary>The layout store — used to seed the sample's card.</summary>
    public ISurfaceStore Surfaces { get; }

    /// <summary>Resolves and writes the ToolKit's <c>.kcs</c> files.</summary>
    public IToolkitWorkflowFileStore WorkflowFiles { get; }

    /// <summary>The client-side projection folded from what the server said.</summary>
    public ToolkitView View { get; } = new();

    /// <summary>
    /// The renderer, when there is one. Setting it re-applies the current projection.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>--self-check</c> runs with no window and therefore no control, and the
    /// protocol→projection half is worth checking on its own.
    /// </remarks>
    public SurfaceCard? Card { get; private set; }

    /// <summary>The ToolKit this session is attached to, or <see langword="null"/>.</summary>
    public string? AttachedToolkitId { get; private set; }

    /// <summary>Raised after <see cref="Card"/> was changed by an incoming message.</summary>
    public event EventHandler<ViewMessage>? MessageApplied;

    /// <summary>Gives this session a renderer and pushes the current projection into it.</summary>
    public void AttachCard(SurfaceCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        Card = card;
        card.SetDocument(View.Surface);

        foreach (var (key, value) in View.Values)
            card.SetValue(key, value);
    }

    /// <summary>Asks the server which ToolKits a View may attach to.</summary>
    /// <returns>The rows, or a refusal's message.</returns>
    public async Task<(IReadOnlyList<ToolkitSummary> Rows, string? Error)> ListAsync()
    {
        var channel = EnsureChannel();

        var reply = await channel.SendAsync(new ListMessage());

        if (!reply.Succeeded)
            return ([], reply.ErrorMessage ?? reply.ErrorCode);

        return (Assert<ToolkitsMessage>(reply.Answer).Toolkits, null);
    }

    /// <summary>Attaches to one ToolKit and folds the handshake into the projection.</summary>
    /// <returns><see langword="null"/> on success, otherwise the refusal's message.</returns>
    public async Task<string?> ConnectAsync(string toolkitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitId);

        var channel = EnsureChannel();

        var reply = await channel.SendAsync(new ConnectMessage(toolkitId));

        if (!reply.Succeeded)
            return reply.ErrorMessage ?? reply.ErrorCode;

        AttachedToolkitId = toolkitId;
        Apply(Assert<WelcomeMessage>(reply.Answer));

        return null;
    }

    /// <summary>Sends one command and reports a refusal.</summary>
    public async Task<string?> SendAsync(CommandMessage command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reply = await EnsureChannel().SendAsync(command);

        return reply.Succeeded ? null : reply.ErrorMessage ?? reply.ErrorCode;
    }

    /// <summary>Creates and mounts the sample ToolKit when it is not already on disk.</summary>
    /// <returns>The sample's id, and whether it had to be created.</returns>
    /// <remarks>
    /// <b>Async because writing the <c>.kcs</c> bodies is async, and this is called from a thread that
    /// has a <c>SynchronizationContext</c>.</b> It used to block internally
    /// (<c>SaveAsync(...).GetAwaiter().GetResult()</c>) and deadlocked the very first launch: the
    /// continuation of the file write was posted back to the Avalonia UI thread, which was sitting in
    /// <c>GetResult()</c> waiting for it. The symptom was a window that never drew and a
    /// <c>scan.kcs.tmp</c> on disk with no <c>scan.kcs</c>.
    /// </remarks>
    public Task<(string Id, bool Created)> EnsureSampleAsync() => SampleToolkit.EnsureAsync(this);

    /// <summary>Seeds the sample ToolKit's card, replacing anything the user edited.</summary>
    public void SeedSampleSurface(string toolkitId) => SampleToolkit.SeedSurface(this, toolkitId);

    private IViewChannel EnsureChannel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _channel ??= Subscribe(_newChannel());
    }

    /// <summary>
    /// Folds one server→client message into the projection and, when present, the renderer.
    /// </summary>
    /// <remarks>
    /// <b>Both sides are fed the same message rather than one reading the other.</b> Handing the
    /// card <c>View.Values</c> instead would make the card agree with the projection by
    /// construction, and the one failure worth catching — a message the projection accepts and the
    /// renderer rejects — would be impossible to observe. The two are allowed to disagree here
    /// precisely so that a test can assert they do not.
    /// </remarks>
    private void Apply(ViewMessage message)
    {
        View.Apply(message);

        if (Card is { } card)
            card.Apply(message);

        MessageApplied?.Invoke(this, message);
    }

    private IViewChannel Subscribe(IViewChannel channel)
    {
        // Every server→client message goes through Apply, including `surface`. A `surface` message
        // replaces the whole layout (Surface.md:818) and carries no values, so a renderer that just
        // rebuilt its cells has nothing to show afterwards. Re-sending the values is NOT done here:
        // it has to happen on the UI thread, and this callback runs on whatever thread the store
        // raised on. HostWindow.OnMessage owns that half, next to the controls that need it. A
        // session with no card (--self-check, and every test that only inspects the projection) has
        // nothing to re-send, so the split costs nothing there.
        channel.Received += (_, message) => Apply(message);

        return channel;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        (_channel as IDisposable)?.Dispose();
        _provider.Dispose();
    }

    /// <summary>Narrows a reply to the type the request is documented to answer with.</summary>
    private static T Assert<T>(ViewMessage? answer) where T : ViewMessage =>
        answer as T ?? throw new InvalidOperationException(
            $"expected {typeof(T).Name}, got {answer?.GetType().Name ?? "nothing"}");
}
