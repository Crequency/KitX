using System.Text.Json;
using KitX.ToolKit.Contract.View;

namespace KitX.ToolKit.View.Avalonia.Host;

/// <summary>
/// Drives the whole chain with no window: compose, mount, list, connect, then a real command.
/// </summary>
/// <remarks>
/// <para><b>Why this exists next to the window.</b> A window can only be checked by a person looking
/// at it, and a person looking at it cannot tell "the projection is right" from "the renderer drew
/// something". This runs the same <see cref="HostSession"/> the window runs and prints what came
/// back, so the protocol half is checkable on a machine with no desktop — including CI.</para>
///
/// <para><b>What it deliberately does not do.</b> It does not open a window, so it cannot say the
/// rendering is correct. That is what <c>KitX.ToolKit.View.Avalonia.Shots</c> is for, and what the
/// Avalonia test project asserts geometrically. Three tools, three questions.</para>
/// </remarks>
internal static class SelfCheck
{
    internal static async Task<int> Run(bool verbose)
    {
        var root = Path.Combine(Path.GetTempPath(), "kitx-view-host-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var session = new HostSession(root);

            var (id, created) = await session.EnsureSampleAsync();
            session.SeedSampleSurface(id);
            session.SeedSampleValues(id);

            Say($"storage root      {root}");
            Say($"sample toolkit    {id} ({(created ? "created" : "already on disk")})");
            Say($"mounted           {session.Toolkits.IsMounted(id)}");

            // Order matters and is the point of the ruling: before the mount, `list` is empty.
            var (rows, listError) = await session.ListAsync();

            if (listError is not null)
                return Fail($"list was refused: {listError}");

            Say($"list              {rows.Count} row(s): {string.Join(", ", rows.Select(r => r.Id))}");

            if (rows.Count != 1 || rows[0].Id != id)
                return Fail($"expected exactly the mounted sample, got {rows.Count} row(s)");

            var connectError = await session.ConnectAsync(id);

            if (connectError is not null)
                return Fail($"connect was refused: {connectError}");

            Say($"connected         {session.AttachedToolkitId}");
            Say($"surface elements  {session.View.Surface.Elements.Count}");

            if (session.View.Surface.Elements.Count == 0)
                return Fail("the card came back empty — the surface was never seeded or never loaded");

            // The first frame, before any command: a card with no values draws eight empty cells and
            // a person reads that as "broken", not as "untouched". `welcome` is the only message that
            // can arrive with values already in it, which is why they are seeded before connecting.
            var opening = session.View.Values.Count;

            Say($"opening values    {opening}");

            if (opening == 0)
                return Fail(
                    "the card connected with no values at all — a first run shows empty cells, which " +
                    "reads as a broken renderer rather than as a ToolKit nobody has used yet");

            // A real command through the same path a click takes. This is what an appended delta is
            // downstream of: without a body that appends, the projection's append branch is only
            // ever exercised by tests that build the message by hand.
            //
            // The status before the click is captured first. Since the sample now seeds an opening
            // status, "status exists" is true before anything runs — waiting on it would be a wait
            // that is already satisfied, and the check would pass with the flow never having run.
            var before = session.View.TryGetValue("status", out var starting) ? starting.GetString() : null;

            var commandError = await session.SendAsync(new CommandMessage(ViewCommands.Event)
            {
                EventName = "scan",
            });

            if (commandError is not null)
                return Fail($"the 'scan' event was refused: {commandError}");

            Say("event 'scan'      accepted");

            // Accepted is not the same as done: `RaiseEvent` starts a flow and returns, and that
            // flow's body has to be compiled before it can write anything. Waiting for the value to
            // *change* is what distinguishes "the command was routed" from "the chain ran".
            var wrote = await WaitForAsync(
                () => session.View.TryGetValue("status", out var now) && now.GetString() != before,
                Patience);

            Say($"status key        {(wrote ? "arrived through the projection" : "did not arrive")}");

            if (!wrote)
                return Fail(
                    "the 'scan' flow never wrote 'status' — the event reached the server but its " +
                    "compiled body did not run, or its delta was not folded into the projection");

            if (verbose)
            {
                foreach (var (key, value) in session.View.Values)
                    Say($"  value {key,-10} {value.GetRawText()}");
            }

            Say("");
            Say("ok — protocol, projection and mount order all agree");

            return 0;
        }
        catch (Exception ex)
        {
            return Fail($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* best-effort temp cleanup */ }
        }
    }

    private static void Say(string message) => Console.WriteLine(message);

    /// <summary>How long to wait for a spawned flow to reach the data plane.</summary>
    /// <remarks>
    /// Generous because the first compile in a process pays Roslyn's warm-up — measured at several
    /// seconds on this machine. A short timeout here would report "the chain is broken" for a
    /// correct chain that was merely slow, which is the kind of false red that teaches people to
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
