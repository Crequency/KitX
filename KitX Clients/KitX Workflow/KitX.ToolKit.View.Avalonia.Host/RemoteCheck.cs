using System.Net.WebSockets;
using System.Text.Json;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View.Server;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// The same four steps <see cref="SelfCheck"/> runs, over a real socket instead of a container.
/// </summary>
/// <remarks>
/// <para><b>Why this is a separate program rather than a flag on the in-process check.</b> The two
/// paths must print the <em>same</em> lines for everything except where the storage is, and the only
/// way a person can see that is to run both and put the output side by side. A flag that switched
/// behaviour inside <see cref="SelfCheck"/> would still be one program whose branches nobody
/// compares; two entry points make the comparison the deliverable.</para>
///
/// <para><b>What it cannot do: seed.</b> A remote client cannot create a ToolKit, write a card or
/// set a value — those are the server's. So this runs against whatever is already mounted on the
/// server it connects to, which is why the first thing it prints is what it found rather than what
/// it made.</para>
///
/// <para><b>Order is still load-bearing.</b> <c>list</c> before <c>connect</c>, and the check that
/// the list is non-empty is the same one the in-process path makes: since 2026-11-11 a View sees
/// only mounted ToolKits, so an empty list means nothing is mounted rather than nothing exists.</para>
/// </remarks>
internal static class RemoteCheck
{
    internal static async Task<int> Run(int port, bool verbose)
    {
        try
        {
            using var socket = new ClientWebSocket();

            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);

            using var channel = new SocketChannel(socket);
            using var session = new RemoteSession(channel);

            Say($"endpoint          ws://127.0.0.1:{port}/");

            var (rows, listError) = await session.ListAsync();

            if (listError is not null)
                return Fail($"list was refused: {listError}");

            Say($"list              {rows.Count} row(s): {string.Join(", ", rows.Select(r => r.Id))}");

            if (rows.Count == 0)
                return Fail(
                    "the server has nothing mounted, so there is nothing to show — a View sees only " +
                    "mounted ToolKits (2026-11-11), and mounting is not a wire command");

            // Connect to the first mounted row rather than to a known id: a remote client does not
            // get to name what it wants if the server says nothing is there.
            var id = rows[0].Id;

            var connectError = await session.ConnectAsync(id);

            if (connectError is not null)
                return Fail($"connect was refused: {connectError}");

            Say($"connected         {session.AttachedToolkitId}");

            var elements = session.View.Surface.Elements;

            Say($"surface elements  {elements.Count}");

            // Unlike the in-process check this does not fail on an empty surface: a server may be
            // running a ToolKit that genuinely has no card, and refusing to report that would make
            // the check disagree with the protocol. It reports the number and moves on.
            Say($"opening values    {session.View.Values.Count}");

            // The event to raise comes from the card, not from a name this program happens to know.
            // `SampleToolkit`'s 'scan' trigger is a property of one fixture; a real server mounted a
            // ToolKit whose buttons name their own events, and those names are in the `welcome`
            // frame's surface. Hard-coding 'scan' made this check report "the chain is broken" for a
            // healthy chain whenever the server was something other than the sample.
            var eventName = elements
                .SelectMany(element => element switch
                {
                    SurfaceButtonElement button => button.Events,
                    SurfaceInputElement input => input.Events,
                    _ => [],
                })
                .FirstOrDefault();

            if (eventName is null)
                return Fail(
                    $"the card of '{id}' declares no button or input event, so there is nothing to " +
                    "raise — a View can only send what the card says it can send");

            Say($"raising event     '{eventName}' (from the card)");

            // Whether anything listens for it is the ack's answer, not something to wait 60 seconds
            // for: `started` is the list of flows this name woke, and an empty list is a complete and
            // immediate answer meaning "no trigger matched".
            var before = Snapshot(session);

            var commandError = await session.SendAsync(new CommandMessage(ViewCommands.Event)
            {
                EventName = eventName,
            });

            if (commandError is not null)
                return Fail($"the '{eventName}' event was refused: {commandError}");

            var started = StartedCount(session);

            if (started is null)
                return Fail(
                    "the ack for the event carried no 'started' value — the server accepted the " +
                    "command but did not say whether a trigger matched");

            Say($"event '{eventName}'      accepted, {started} flow(s) started");

            if (started == 0)
            {
                // Not a failure of the wire: the name is on the card and no trigger of the ToolKit
                // answers to it. That is a fact about the ToolKit, and this program's job is to
                // report which of the two it is instead of timing out and blaming the socket.
                Say("status key        not raised — no trigger answers to that name");

                Say("");
                Say("ok — protocol and projection agree; the card's event has no trigger behind it");

                return 0;
            }

            var wrote = await WaitForAsync(() => Snapshot(session) != before, Patience);

            Say($"status key        {(wrote ? "changed through the projection" : "did not change")}");

            if (verbose)
            {
                foreach (var (key, value) in session.View.Values)
                    Say($"  value {key,-10} {value.GetRawText()}");
            }

            Say("");
            Say("ok — protocol, projection and mount order all agree");

            return wrote ? 0 : Fail(
                $"the '{eventName}' flow never wrote a value — the event reached a trigger but its " +
                "compiled body did not run, or its delta was not folded into the projection");
        }
        catch (Exception ex)
        {
            return Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Every value the projection currently holds, flattened for comparison.</summary>
    /// <remarks>
    /// A string rather than a dictionary: the question is only "did anything change", and a snapshot
    /// built from the same projection it is compared against cannot disagree with itself in a way
    /// that matters.
    /// </remarks>
    private static string Snapshot(RemoteSession session) =>
        string.Join(
            "\u001f",
            session.View.Values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value.GetRawText()));

    /// <summary>How many flows the last ack said it started, or null if the ack did not say.</summary>
    /// <remarks>
    /// Null means "the ack did not answer that", which is a different thing from zero flows. Both are
    /// reported, and the caller says which; collapsing them into one number is how "no trigger
    /// matched" would come to read like "the chain is broken".
    /// </remarks>
    private static int? StartedCount(RemoteSession session)
    {
        if (session.LastAck?.Values is not { } values)
            return null;

        return values.TryGetValue("started", out var started) ? started.GetArrayLength() : null;
    }

    private static void Say(string message) => Console.WriteLine(message);

    /// <summary>How long to wait for a spawned flow to reach the data plane.</summary>
    /// <remarks>
    /// The same patience as the in-process check, for the same reason: the first compile in a
    /// process pays Roslyn's warm-up. A shorter deadline here would report "the chain is broken" for
    /// a correct chain that was merely slow, which is the class of false red that teaches people to
    /// re-run instead of read.
    /// </remarks>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }

    private static int Fail(string reason)
    {
        Console.Error.WriteLine("FAILED: " + reason);
        return 1;
    }
}
