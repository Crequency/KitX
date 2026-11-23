namespace KitX.ToolKit.View.Avalonia.Host;

using System.Net.WebSockets;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View.Server;

/// <summary>
/// Opens the socket a remote window needs, or explains why it could not.
/// </summary>
/// <remarks>
/// <para><b>Why the connect happens here rather than in the window.</b> <see cref="RemoteSession"/>
/// takes a channel that is already connected, and connecting is an <c>await</c>. Doing it on the UI
/// thread inside the window's constructor would mean blocking the dispatcher before it pumps a
/// single message; doing it in <c>Main</c>, before the lifetime starts, means the wait happens where
/// waits are allowed.</para>
///
/// <para><b>A failure returns a channel rather than <see langword="null"/>.</b> The window's whole
/// point is to make the chain visible, including when it breaks — and a refused <c>list</c> already
/// has a status line. Returning null would force the window to grow a second failure path; refusing
/// locally reuses the one it has. <see cref="UnreachableChannel"/> is that path.</para>
///
/// <para><b>The timeout is short on purpose.</b> A socket to a closed port fails immediately on this
/// machine, so the timeout only matters for a host that answers TCP and then says nothing. Five
/// seconds is long enough for a loaded machine and short enough that a person does not conclude the
/// program hung.</para>
/// </remarks>
internal static class RemoteConnect
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>Connects, or returns a channel that refuses everything with the reason.</summary>
    internal static async Task<IViewChannel> OpenAsync(int port)
    {
        var socket = new ClientWebSocket();

        using var deadline = new CancellationTokenSource(Patience);

        try
        {
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), deadline.Token);

            return new SocketChannel(socket);
        }
        catch (Exception ex)
        {
            socket.Dispose();

            // The message a person gets names the address they asked for. "Connection refused" alone
            // leaves them wondering whether the port or the program was wrong.
            var reason = ex is OperationCanceledException
                ? $"ws://127.0.0.1:{port}/ did not answer within {Patience.TotalSeconds:0} seconds."
                : $"ws://127.0.0.1:{port}/ could not be reached: {ex.Message}";

            return new UnreachableChannel(reason);
        }
    }
}
