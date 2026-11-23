using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// The six things the window needs from a session, and nothing else.
/// </summary>
/// <remarks>
/// <para><b>Why the interface is this narrow.</b> A window draws a card and forwards clicks. That
/// needs a projection, an id to say what is attached, a way to hear about pushes, and the three
/// requests that reach a server. It does not need a container, a storage root, a key store or a
/// workflow file store — those belong to whoever <em>is</em> the server. Narrowing here is the whole
/// point of the change: what a window gets is what a window actually uses.</para>
///
/// <para><b>What is deliberately not a member.</b> <c>Toolkits</c>, <c>Surfaces</c>, <c>Keys</c>,
/// <c>WorkflowFiles</c> and <c>Root</c> stay on <see cref="HostSession"/>, and <c>LastAck</c> and
/// <c>Surface</c> stay on <see cref="RemoteSession"/>. Putting any of them here would give every
/// implementer a property only one of them can answer honestly, which is the
/// four-comments-instead-of-four-facts shape <see cref="RemoteSession"/>'s own remarks argue
/// against.</para>
///
/// <para><b>Seeding is not here either, on purpose.</b> <see cref="HostSession"/> can create a
/// ToolKit, write its card and fill its keys because it owns the container that does those things. A
/// remote client cannot do any of them, and a member that threw in one implementation would make the
/// window's start-up path a type test. Those three steps became a constructor parameter instead —
/// see <c>HostWindow</c>'s <c>prepare</c>.</para>
/// </remarks>
internal interface IViewSession
{
    /// <summary>The client-side projection folded from what the server said.</summary>
    ToolkitView View { get; }

    /// <summary>The ToolKit this session is attached to, or <see langword="null"/>.</summary>
    string? AttachedToolkitId { get; }

    /// <summary>Raised after every server→client message was folded into the projection.</summary>
    /// <remarks>
    /// On whatever thread the message arrived on — a store's callback for the in-process session, a
    /// socket's receive loop for the remote one. A subscriber that owns controls has to marshal; the
    /// window does.
    /// </remarks>
    event EventHandler<ViewMessage>? MessageApplied;

    /// <summary>Asks the server which ToolKits a View may attach to.</summary>
    /// <returns>The rows, or a refusal's message.</returns>
    Task<(IReadOnlyList<ToolkitSummary> Rows, string? Error)> ListAsync();

    /// <summary>Attaches to one ToolKit and folds the handshake into the projection.</summary>
    /// <returns><see langword="null"/> on success, otherwise the refusal's message.</returns>
    Task<string?> ConnectAsync(string toolkitId);

    /// <summary>Sends one command and reports a refusal.</summary>
    Task<string?> SendAsync(CommandMessage command);
}
