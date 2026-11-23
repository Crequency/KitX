using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View.Avalonia.Host;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// The window draws what the server said, and seeds nothing of its own.
/// </summary>
/// <remarks>
/// <para><b>Why these two assertions and not "the window opens".</b> A window that opens is the easy
/// half. The two things that can be wrong without anyone noticing are: it draws its <em>own</em>
/// sample instead of the server's card, and it writes to the server during start-up. Neither throws,
/// neither looks broken, and both would make this host a second server rather than a client.</para>
///
/// <para><b>A hand-written channel, not a real <c>ViewServer</c>.</b> Whether the server works is
/// <c>SocketChannelTests</c>' question, answered against a real port. The question here is what the
/// window does with an answer, so the answer is fixed and the transport is absent — a real server
/// would add a socket, a thread and a timeout to an assertion about which document got drawn.</para>
/// </remarks>
public sealed class RemoteWindowTests
{
    /// <summary>Two elements, one label, and nothing in common with the in-process sample.</summary>
    private const string ServerCard = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Text",   "key": "title", "row": 1, "weight": 1 },
            { "type": "Button", "label": "server", "events": ["Submit"], "row": 2, "weight": 1 }
          ]
        }
        """;

    private const string ServerToolkitId = "server-toolkit";

    [AvaloniaFact]
    public void The_remote_window_shows_what_the_server_said()
    {
        var channel = new FixedChannel(Card());
        using var session = new RemoteSession(channel);
        var window = Open(session, prepare: null);

        var card = CardOf(window);

        // Two, not the sample's eight. A window that fell back to its own sample would draw eight
        // elements and three rows of text a person could read — which is exactly why this is counted
        // rather than eyeballed.
        Assert.Equal(2, CardProbe.Elements(card).Count);

        // The label, because a count alone cannot tell the server's card from a card of the same
        // size. "server" appears nowhere in the in-process sample.
        Assert.Equal(
            "server",
            CardProbe.Elements(card).OfType<Button>().Single().Content as string);
    }

    [AvaloniaFact]
    public void A_window_with_no_prepare_step_writes_nothing()
    {
        var channel = new FixedChannel(Card());
        using var session = new RemoteSession(channel);
        var window = Open(session, prepare: null);

        // Reading the card is what makes this a started window rather than a window that has not
        // run yet: StartAsync has to have finished for the list to be filled.
        _ = CardOf(window);

        // Only the two requests the window is allowed to make. A prepare step on the remote path
        // would be the client writing a card it does not own, and it would show up here as a third
        // message — which is also why this compares names rather than a count: a count would stay
        // at two if one request were replaced by another.
        Assert.Equal(
            [nameof(ListMessage), nameof(ConnectMessage)],
            channel.Sent.Select(message => message.GetType().Name).ToArray());
    }

    private static SurfaceCard CardOf(Window window) =>
        window.GetVisualDescendants().OfType<SurfaceCard>().Single();

    /// <summary>Shows the window and returns it once the server's handshake has been applied.</summary>
    /// <remarks>
    /// <para><b>Measured rather than assumed, because the loop is a fallback on this chain.</b> The
    /// probe reading right after <c>Show()</c> returned, with no pump at all: children 2,
    /// sent [ListMessage, ConnectMessage], applied [WelcomeMessage]. The whole chain — SelectedIndex
    /// → SelectionChanged → ConnectAsync → an already-completed ValueTask → Apply → Forward on the UI
    /// thread → SurfaceCard.Apply — runs inside Show(). So the loop below never executes today, and
    /// that is written down rather than left to be discovered.</para>
    ///
    /// <para><b>Why it stays anyway.</b> The day somebody puts an <c>await Task.Yield()</c> between
    /// those steps the chain becomes a scheduled one, and without a pump it would be a test that
    /// passes or fails by timing. A mutation adding exactly that yield is expected to stay <em>green</em>
    /// — it is the one thing here that must survive being made asynchronous, which is what separates
    /// it from the two mutations this test exists to catch.</para>
    ///
    /// <para><b>The condition is a message, not a drawn card.</b> <c>Children.Count &gt; 0</c> is a
    /// second-order effect of the welcome arriving; the first-order fact is that the window sent its
    /// connect and the fixed channel answered synchronously. Waiting on the visible consequence is
    /// still the right condition for a UI test — it is what a person sees — but the reading above is
    /// why the wait has no clock in it today.</para>
    /// </remarks>
    private static Window Open(RemoteSession session, Func<Task<string?>>? prepare)
    {
        var window = new HostWindow(session, prepare) { ShowInTaskbar = false };

        window.Show();

        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            // Checked before the pump, because on today's synchronous chain the state is already
            // final here and the sleep below would be pure delay.
            if (window.GetVisualDescendants().OfType<SurfaceCard>()
                .Any(card => card.Children.Count > 0))
            {
                return window;
            }

            Dispatcher.UIThread.RunJobs();

            // Only after a full pump came back empty, so the synchronous case never sleeps.
            Thread.Sleep(10);
        }

        // The status bar is where StartAsync puts an exception it caught, so a timeout that does
        // not quote it reports the symptom and throws the cause away.
        var said = string.Join(
            " | ",
            window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        Assert.Fail(
            "the window never drew a card: StartAsync did not complete within ten seconds, so " +
            "nothing after this point would be measuring start-up behaviour. It says: " + said);

        return window;
    }

    private static SurfaceDocument Card() =>
        JsonSerializer.Deserialize<SurfaceDocument>(ServerCard)!;

    /// <summary>
    /// A channel whose answers are fixed and whose sent messages are recorded.
    /// </summary>
    /// <remarks>
    /// It answers exactly the two requests a window start-up makes. Anything else is recorded and
    /// refused, so the "writes nothing" assertion has a place to see the extra message before the
    /// window's error path turns it into a status line.
    /// </remarks>
    private sealed class FixedChannel : IViewChannel
    {
        private readonly SurfaceDocument _card;

        internal FixedChannel(SurfaceDocument card) => _card = card;

        /// <summary>Every message the window sent, in order.</summary>
        internal List<ViewMessage> Sent { get; } = [];

        public event EventHandler<ViewMessage>? Received { add { } remove { } }

        public ValueTask<ChannelReply> SendAsync(
            ViewMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);

            return new ValueTask<ChannelReply>(message switch
            {
                ListMessage => ChannelReply.Accepted(new ToolkitsMessage(
                [
                    new ToolkitSummary(ServerToolkitId, "server", LifecycleState.Enabled),
                ])),

                ConnectMessage connect => ChannelReply.Accepted(new WelcomeMessage(
                    connect.ToolkitId,
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                    _card)),

                _ => ChannelReply.Rejected(
                    ViewErrorCodes.UnknownCommand, $"this window should not send {message.GetType().Name}"),
            });
        }
    }
}
