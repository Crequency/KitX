using System.Text.Json;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View.Avalonia.Host;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The chain the host exists to make visible: mount → list → connect → delta → projection.
/// </summary>
/// <remarks>
/// <para><b>Why these assertions belong to the host and not to the protocol tests.</b> The protocol
/// tests assert what a channel answers; the projection tests assert what a <c>ToolkitView</c> folds.
/// Neither can say the two were ever connected, and on 2026-11-11 that gap produced a real defect:
/// <c>appended</c> meant "whole replacement" on one side and "one more item" on the other, with
/// every test green, because each side was checked against a hand-built message the other never
/// sent.</para>
///
/// <para><b>Every test here drives the same object the window drives</b> — <see cref="HostSession"/>,
/// not a hand-assembled channel. That is the point: the arrangement under test is the one that
/// ships.</para>
///
/// <para><b>What is deliberately absent: the renderer.</b> <c>SurfaceCard</c> needs a UI thread, and
/// this project is not an Avalonia test project. The renderer half of the same seam is pinned in
/// <c>KitX.ToolKit.View.Avalonia.Test.Xunit</c> against messages parsed from bytes. Splitting it
/// that way keeps each project's stated boundary intact instead of quietly widening one of them to
/// make a single test easier to write.</para>
/// </remarks>
[Trait("Category", "Integration")]
public class HostSessionTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "kitx-host-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task List_is_empty_until_the_sample_is_mounted()
    {
        // The ruling of 2026-11-11, stated as an ordering constraint: a View sees only mounted
        // ToolKits, and `mount` is not on the wire. So "list first, mount after" is not merely
        // unhelpful — it is this empty list, with no error to explain it.
        using var session = new HostSession(_root);

        var (before, beforeError) = await session.ListAsync();

        Assert.Null(beforeError);
        Assert.Empty(before);

        await session.EnsureSampleAsync();

        var (after, afterError) = await session.ListAsync();

        Assert.Null(afterError);
        Assert.Single(after);
        Assert.Equal(SampleToolkit.Id, after[0].Id);
    }

    [Fact]
    public async Task Connect_delivers_the_card_the_sample_declares()
    {
        using var session = new HostSession(_root);

        await session.EnsureSampleAsync();
        session.SeedSampleSurface(SampleToolkit.Id);

        Assert.Null(await session.ConnectAsync(SampleToolkit.Id));

        Assert.Equal(SampleToolkit.Id, session.AttachedToolkitId);
        Assert.Equal(SampleToolkit.Id, session.View.ToolkitId);
        Assert.Equal(4, session.View.Surface.Elements.Count);
    }

    [Fact]
    public async Task A_button_event_reaches_the_data_plane_and_comes_back_as_a_delta()
    {
        // The whole chain in one test: a wire command → the real executor compiles and runs the
        // body → the body writes → the store raises → the session forwards → the projection folds.
        // Every arrow has been tested in isolation somewhere; only this asserts they compose.
        using var session = new HostSession(_root);

        await session.EnsureSampleAsync();
        session.SeedSampleSurface(SampleToolkit.Id);
        await session.ConnectAsync(SampleToolkit.Id);

        Assert.Null(await session.SendAsync(new CommandMessage(ViewCommands.Event)
        {
            EventName = "scan",
        }));

        var arrived = await WaitUntilAsync(() => session.View.TryGetValue("status", out _));

        Assert.True(arrived,
            "the 'scan' flow never wrote 'status'. The event was accepted, so the break is downstream "
            + "of the channel: the compiled body failed, or its delta never reached the projection.");

        Assert.True(session.View.TryGetValue("status", out var status));
        Assert.Equal("扫描了一次", status.GetString());
    }

    [Fact]
    public async Task An_appended_key_grows_one_item_per_event_instead_of_being_replaced()
    {
        // `log` is a Stream key and each press appends exactly one line. Two presses is the smallest
        // count that separates append from replace: at one press both folds leave a one-element
        // array, and at two a replacement leaves one again — which is exactly how the original
        // defect survived, because every test happened to use a single frame.
        using var session = new HostSession(_root);

        await session.EnsureSampleAsync();
        session.SeedSampleSurface(SampleToolkit.Id);
        await session.ConnectAsync(SampleToolkit.Id);

        for (var press = 1; press <= 2; press++)
        {
            Assert.Null(await session.SendAsync(new CommandMessage(ViewCommands.Event)
            {
                EventName = "scan",
            }));

            var expected = press;

            Assert.True(
                await WaitUntilAsync(() => session.View.TryGetValue("log", out var log)
                                          && log.ValueKind == JsonValueKind.Array
                                          && log.GetArrayLength() == expected),
                $"after {press} press(es) 'log' should hold {expected} entry(s); a replacement fold "
                + "would hold one no matter how many times it pressed");
        }
    }

    /// <summary>Polls until the condition holds. Generous: a first compile in a process pays Roslyn's warm-up.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int seconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }
}
