using System.Collections.Generic;
using System.Text.Json;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.View.Server;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Two settings in <see cref="ViewWire.Options"/> that a reader can see in the bytes, and the
/// reason the other settings are not pinned here.
/// </summary>
/// <remarks>
/// <para><b>Why these two and not the rest.</b> <c>WhenWritingNull</c> and
/// <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> both change what the wire
/// <em>is</em>; a peer that disagrees about either one sees different bytes for the same message.
/// <c>PropertyNameCaseInsensitive</c> does not: it only affects reading, and its correct criterion
/// is behavioural — "capitalise the field names in the bytes, can they still be read" — not a
/// snapshot of an option. A snapshot of every setting is an assertion that rots: it goes red when
/// somebody adds an option, and the red says nothing about whether the wire is still right.</para>
///
/// <para><b>Why <c>DoesNotContain</c> rather than "it reads back as null".</b> Reading back is the
/// weaker assertion by a wide margin: <c>null</c> and <em>absent</em> both deserialise to
/// <see langword="null"/>, so a round-trip would stay green with null-suppression removed. The
/// setting is about which spelling is <em>written</em>, so the assertion has to be about the text.
/// This is the same shape as the earlier lesson about asserting the value *and* the sentence: the
/// assertion catches exactly the mutations that change the thing it names.</para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ViewWireOptionsTests
{
    private static WelcomeMessage Welcome() => new(
        "tk-options",
        new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("v") },
        new SurfaceDocument
        {
            SchemaVersion = 1,
            LayoutVersion = 1,
            Elements =
            [
                // Format, Role and Name all left null on purpose: those are the three nullable
                // fields on SurfaceTextElement, and they are what null-suppression acts on.
                new SurfaceTextElement { Row = 1, Weight = 1, Key = "title" },
            ],
        });

    [Fact]
    public void A_null_member_of_an_element_is_written_as_nothing_rather_than_as_null()
    {
        var json = JsonSerializer.Serialize<ViewMessage>(Welcome(), ViewWire.Options);

        // The three nullable fields of the element. If WhenWritingNull is dropped from the options,
        // each of these appears as "format":null and the test goes red — which is the direction
        // this assertion exists to move in.
        Assert.DoesNotContain("\"format\"", json);
        Assert.DoesNotContain("\"role\"", json);
        Assert.DoesNotContain("\"name\"", json);

        var back = Assert.IsType<WelcomeMessage>(
            JsonSerializer.Deserialize<ViewMessage>(json, ViewWire.Options));

        // And what a reader gets back: the element, with the three fields null. Absent and null are
        // the same message today, which is the whole reason this trade-off is acceptable — and the
        // reason the assertion above has to be about the *text*. Asserting only this half would be
        // green with the suppression removed, because both spellings deserialise to null.
        var text = Assert.IsType<SurfaceTextElement>(back.Surface.Elements[0]);
        Assert.Null(text.Format);
        Assert.Null(text.Role);
        Assert.Null(text.Name);
    }

    [Fact]
    public void The_state_of_a_toolkit_travels_as_its_name_rather_than_its_number()
    {
        var frame = new { type = "toolkits", toolkits = new[] { new { id = "tk", name = "n", state = LifecycleState.Enabled } } };
        var json = JsonSerializer.Serialize(frame, ViewWire.Options);

        // `state` is also written as a number by every other serializer that does not register the
        // converter, and `Installed`/`Enabled`/`Disabled` have been written to disk as names in
        // three other places. A View client written against those files would read "state": 1 here
        // and see a value it has no case for.
        Assert.Contains("\"state\":\"Enabled\"", json);
        Assert.DoesNotContain("\"state\":1", json);
    }
}
