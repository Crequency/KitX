using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;
using Xunit;

namespace KitX.ToolKit.View.Test.Xunit;

/// <summary>
/// The whole point of a separate View library: a projection can be driven with a list of messages
/// and checked, with no Dashboard, no Core and no server.
/// </summary>
[Trait("Category", "Unit")]
public class ToolkitViewTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static WelcomeMessage Welcome(string toolkitId, params (string Key, string Json)[] values)
        => new(
            toolkitId,
            values.ToDictionary(v => v.Key, v => Json(v.Json), StringComparer.Ordinal),
            SurfaceDocument.Empty);

    [Fact]
    public void Welcome_three_keys_then_a_delta_then_a_removal_leaves_one_key()
    {
        var view = new ToolkitView();

        Assert.True(view.Apply(Welcome("tk", ("a", "\"one\""), ("b", "\"two\""), ("c", "\"three\""))));
        Assert.Equal(3, view.Values.Count);

        Assert.True(view.Apply(new DeltaMessage("b", Json("\"changed\""))));
        Assert.Equal("changed", view.Values["b"].GetString());

        Assert.True(view.Apply(new DeltaMessage("a", null)));

        Assert.Equal(2, view.Values.Count);
        Assert.Equal("changed", view.Values["b"].GetString());
        Assert.Equal("three", view.Values["c"].GetString());
        Assert.Equal("tk", view.ToolkitId);
    }

    [Fact]
    public void Reconnect_replaces_the_whole_projection_rather_than_merging()
    {
        var view = new ToolkitView();
        view.Apply(Welcome("tk", ("stale", "\"gone\""), ("kept", "\"old\"")));

        // A reconnect gets a fresh welcome. The key the server no longer mentions must disappear —
        // merging would keep "stale" alive on the client for the rest of the session.
        view.Apply(Welcome("tk", ("kept", "\"new\"")));

        Assert.Single(view.Values);
        Assert.Equal("new", view.Values["kept"].GetString());
        Assert.False(view.TryGetValue("stale", out _));
    }

    [Fact]
    public void A_surface_message_replaces_the_layout_rather_than_merging()
    {
        var view = new ToolkitView();
        view.Apply(new SurfaceMessage(new SurfaceDocument
        {
            Elements = [new SurfaceTextElement { Key = "a" }, new SurfaceTextElement { Key = "b" }],
        }));

        Assert.Equal(2, view.Surface.Elements.Count);

        view.Apply(new SurfaceMessage(new SurfaceDocument
        {
            Elements = [new SurfaceButtonElement { Label = "only" }],
        }));

        Assert.Single(view.Surface.Elements);
        Assert.IsType<SurfaceButtonElement>(view.Surface.Elements[0]);
    }

    [Fact]
    public void Clearing_removes_values_but_not_the_layout()
    {
        var view = new ToolkitView();
        view.Apply(Welcome("tk", ("a", "\"one\"")) with
        {
            Surface = new SurfaceDocument { Elements = [new SurfaceButtonElement { Label = "go" }] },
        });

        view.Apply(new ClearedMessage("tk"));

        Assert.Empty(view.Values);
        Assert.Single(view.Surface.Elements);
    }

    [Fact]
    public void A_clear_for_another_toolkit_is_ignored()
    {
        var view = new ToolkitView();
        view.Apply(Welcome("tk", ("a", "\"one\"")));

        Assert.False(view.Apply(new ClearedMessage("other")));
        Assert.Single(view.Values);
    }

    [Fact]
    public void Messages_that_carry_no_state_do_not_change_the_projection()
    {
        var view = new ToolkitView();
        view.Apply(Welcome("tk", ("a", "\"one\"")));

        Assert.False(view.Apply(new AckedReply { Command = "trigger" }));
        Assert.False(view.Apply(ChannelReply.Rejected("view.not-attached", "no").Answer
            ?? new RejectedReply { Code = "view.not-attached", Message = "no" }));
        Assert.False(view.Apply(new ListMessage()));
        Assert.False(view.Apply(new ToolkitsMessage([])));

        Assert.Single(view.Values);
    }

    [Fact]
    public void A_delta_before_any_welcome_is_recorded_rather_than_dropped()
    {
        // Out-of-order delivery. Keeping it is more useful than losing it, and the next welcome
        // settles the question either way.
        var view = new ToolkitView();

        Assert.True(view.Apply(new DeltaMessage("early", Json("1"))));
        Assert.False(view.IsAttached);
        Assert.Equal(1, view.Values["early"].GetInt32());
    }

    [Fact]
    public void Values_returns_a_copy_so_a_later_delta_does_not_mutate_it()
    {
        var view = new ToolkitView();
        view.Apply(Welcome("tk", ("a", "1")));

        var snapshot = view.Values;
        view.Apply(new DeltaMessage("a", Json("2")));

        Assert.Equal(1, snapshot["a"].GetInt32());
        Assert.Equal(2, view.Values["a"].GetInt32());
    }

    [Fact]
    public void Appended_values_are_folded_as_replacements()
    {
        // The server already concatenated; `appended` says how the value got there, not that the
        // client should keep a sequence.
        var view = new ToolkitView();
        view.Apply(Welcome("tk", ("log", "[1]")));

        view.Apply(new DeltaMessage("log", Json("[1,2]")) { Appended = true });

        Assert.Equal(2, view.Values["log"].GetArrayLength());
    }
}
