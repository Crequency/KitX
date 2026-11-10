using System.Text.Json;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Storage;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The bytes <c>SurfaceStore</c> writes are the bytes a renderer has to be able to read.
///
/// <para><b>Why this exists, and what it is not.</b> The handover's §3.4 asks for one end-to-end
/// test proving the whole chain — "change weight → save → read back → renderer draws → width
/// changed". That last step cannot be written today: <c>SurfaceCard</c> lives in
/// <c>KitX.ToolKit.View.Avalonia</c>, and no assembly in this solution references both it and
/// <c>KitX.ToolKit</c>. <c>KitX.ToolKit.View.Avalonia.Test.Xunit</c> deliberately does not
/// reference the scheduler assembly (its remark says so), and writing a test project that joins
/// them would create the exact dependency edge ADR-0001 split the assemblies to remove.</para>
///
/// <para>So this test covers the half that is genuinely missing rather than the half that is
/// already covered twice. <c>SurfaceTests.Save_then_load_round_trips_every_element_kind</c>
/// asserts that a document survives a save/load through the store's own serializer;
/// <c>FullCardLayoutTests</c> asserts that a hand-written JSON literal lays out correctly. Neither
/// one looks at the file the store wrote. If the store's serializer and the renderer's reader
/// disagreed about a property name, every existing test would still pass: the round-trip never
/// leaves the store, and the layout test never enters it.</para>
///
/// <para><b>The assertion is about names, not about values.</b> Rendering is out of reach here, so
/// what this pins is the contract the renderer's deserializer actually depends on: the exact JSON
/// property names on disk. A rename that keeps the store self-consistent is invisible to the
/// round-trip test and would render every card blank — this is the test that fails instead.</para>
/// </summary>
[Trait("Category", "Unit")]
public class SurfaceFileShapeTests : IDisposable
{
    private const string TkId = "tk-shape";

    private readonly TempToolkitRoot _root = new();

    public void Dispose()
    {
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The property names <see cref="SurfaceElement"/> writes, as the renderer's reader requires them.
    /// </summary>
    /// <remarks>
    /// Written as literals rather than read from the type on purpose. Reading them back from
    /// <c>JsonPropertyName</c> attributes would make the test compare the attribute table with
    /// itself and pass no matter what the attribute table said — the same reason the builtin
    /// alignment test does not derive its prefix list from the registry.
    /// </remarks>
    private static readonly string[] PlacementNames = ["row", "weight"];

    [Fact]
    public void The_saved_file_uses_the_property_names_a_renderer_reads()
    {
        var store = new SurfaceStore(_root.Root);
        store.Save(TkId, new SurfaceDocument
        {
            Elements =
            [
                new SurfaceTextElement { Key = "t", Row = 2, Weight = 3 },
                new SurfaceButtonElement { Label = "go", Events = ["Click"], Emphasis = "danger", Row = 2, Weight = 1 },
                new SurfaceListElement { Key = "l", ItemTitle = "startedAt", Row = 3, Weight = 4 },
            ],
        });

        var path = Path.Combine(_root.Root, TkId, SurfaceStore.FileName);
        Assert.True(File.Exists(path), "the store did not write a surface file");

        using var parsed = JsonDocument.Parse(File.ReadAllText(path));
        var root = parsed.RootElement;

        // The envelope the reader requires before it will look at any element.
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(SurfaceDocument.CurrentLayoutVersion, root.GetProperty("layoutVersion").GetInt32());

        var elements = root.GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal(3, elements.Count);

        // Every element carries its placement under the names the renderer asks for. A card whose
        // elements all land in row 1 with weight 1 is the visible result of getting these wrong,
        // and nothing before this test would have reported it.
        foreach (var element in elements)
            foreach (var name in PlacementNames)
                Assert.True(
                    element.TryGetProperty(name, out _),
                    $"element '{element.GetProperty("type").GetString()}' is missing '{name}'");

        // The discriminator the file uses to pick an element type, spelled the way the
        // [JsonDerivedType] attributes spell it.
        Assert.Equal(
            ["Text", "Button", "List"],
            elements.Select(e => e.GetProperty("type").GetString()));

        Assert.Equal(2, elements[0].GetProperty("row").GetInt32());
        Assert.Equal(3, elements[0].GetProperty("weight").GetInt32());

        // A button's emphasis is the third of the three values; `quiet` was retired. If the file
        // wrote it as a number or under another name, the renderer would fall back to normal and
        // a destructive action would look safe.
        Assert.Equal("danger", elements[1].GetProperty("emphasis").GetString());
        Assert.Equal("Click", Assert.Single(elements[1].GetProperty("events").EnumerateArray()).GetString());

        // A List's item paths, which are what its rows read from each record.
        Assert.Equal("startedAt", elements[2].GetProperty("itemTitle").GetString());
    }

    [Fact]
    public void What_the_store_wrote_is_what_the_renderer_parser_accepts()
    {
        // The companion to the test above: names being right is necessary, and being *accepted* is
        // the other half. Deserialising the store's own output with the same model type the
        // renderer receives proves the file is not merely shaped correctly but readable — a
        // property emitted as a number where the model expects a string would pass a name check and
        // fail here.
        var store = new SurfaceStore(_root.Root);
        var original = new SurfaceDocument
        {
            Elements =
            [
                new SurfaceTextElement { Key = "t", Row = 2, Weight = 3 },
                new SurfaceButtonElement { Label = "go", Events = ["Click"], Emphasis = "danger" },
            ],
        };

        store.Save(TkId, original);

        var json = File.ReadAllText(Path.Combine(_root.Root, TkId, SurfaceStore.FileName));
        var reread = JsonSerializer.Deserialize<SurfaceDocument>(json);

        Assert.NotNull(reread);
        Assert.Equal(original.Elements.Count, reread!.Elements.Count);

        var text = Assert.IsType<SurfaceTextElement>(reread.Elements[0]);
        Assert.Equal(2, text.Row);
        Assert.Equal(3, text.Weight);

        var button = Assert.IsType<SurfaceButtonElement>(reread.Elements[1]);
        Assert.Equal("danger", button.Emphasis);
        Assert.Equal(["Click"], button.Events);
    }
}
