using System.Text.Json;
using System.Text.Json.Nodes;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Data;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Surface;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// The surface's three failure tiers (design §3.3.4): a file-level defect refuses everything, an
/// element-level defect drops that element, a field-level defect keeps the element and adjusts it.
/// </summary>
/// <remarks>
/// <para>The line between the tiers is "can a renderer still draw something", not severity. These
/// tests exist to hold that line, and the first one holds the sharpest edge of it: an unknown
/// element type must not escalate into a refused file.</para>
///
/// <para>They are written against the store and the validator, not against a private helper, so
/// they would survive either being rewritten internally.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class SurfaceTests : IDisposable
{
    private const string TkId = "tk-surface";

    private readonly TempToolkitRoot _root = new();

    public void Dispose()
    {
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    private SurfaceStore Store() => new(_root.Root);

    private static KeyTable Keys(params KeyDeclaration[] declarations)
        => KeyTable.From(TkId, declarations);

    private void WriteRaw(string json)
    {
        var dir = Path.Combine(_root.Root, TkId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, SurfaceStore.FileName), json);
    }

    // ── file tier ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The regression this whole per-element parsing exists for: <c>JsonPolymorphic</c> throws for
    /// the entire deserialization on an unrecognised discriminator, which would silently promote a
    /// single bad element into a refused file — the worst direction, because one typo blanks the
    /// whole card.
    /// </summary>
    [Fact]
    public void An_unknown_element_type_drops_that_element_and_keeps_the_rest()
    {
        WriteRaw("""
        {
          "schemaVersion": 1,
          "columns": 4,
          "elements": [
            { "type": "Text",        "row": 1, "column": 1, "key": "title" },
            { "type": "Holographic", "row": 1, "column": 2, "key": "x" },
            { "type": "Button",      "row": 2, "column": 1, "label": "go" }
          ]
        }
        """);

        var loaded = Store().Load(TkId);

        Assert.Empty(loaded.Errors);
        Assert.Single(loaded.Warnings);
        Assert.Contains("Holographic", loaded.Warnings[0].Message);

        // Two of the three survive, in their original order.
        Assert.Equal(2, loaded.Document.Elements.Count);
        Assert.IsType<SurfaceTextElement>(loaded.Document.Elements[0]);
        Assert.IsType<SurfaceButtonElement>(loaded.Document.Elements[1]);

        // The clause that makes the whole chain safe to send.
        //
        // A WelcomeMessage carries this document to a client, and the client deserialises it with the
        // very same polymorphic machinery that produced the warning above. That machinery refuses an
        // unknown discriminator by throwing for the whole payload — so if an unknown element ever
        // survived this loader, one bad element would take down an entire welcome message on the
        // other side of the wire, and the drop-plus-warning contract here would be meaningless.
        //
        // Asserting that the survivor count is two says the element was dropped. Asserting that the
        // serialised form no longer names it says the drop is COMPLETE — that nothing downstream can
        // be handed the thing the loader just rejected.
        //
        // This assertion has no mutation that can break it, and that is worth saying out loud rather
        // than leaving as a silent gap: Elements is IReadOnlyList<SurfaceElement>, the seven subtypes
        // are closed, and no eighth type exists to construct. So "let the unknown element through"
        // cannot be written in C# — the count assertion above would catch it first, and this one
        // covers a path the type system already closes. It is here because the guarantee it states
        // is what a reader of WelcomeMessage needs to know, and because the day a plugin-supplied
        // element kind is allowed, the type system stops closing it and this clause starts working.
        var reserialised = JsonSerializer.Serialize(
            loaded.Document, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("Holographic", reserialised);
    }

    [Fact]
    public void Columns_other_than_four_refuse_the_whole_file()
    {
        WriteRaw("""
        {
          "schemaVersion": 1,
          "columns": 6,
          "elements": [ { "type": "Button", "row": 1, "column": 1, "label": "go" } ]
        }
        """);

        var loaded = Store().Load(TkId);

        Assert.Single(loaded.Errors);
        Assert.Equal("surface.file.unsupported-columns", loaded.Errors[0].Code);
        Assert.Empty(loaded.Document.Elements);
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public void An_unsupported_schema_version_refuses_the_whole_file()
    {
        WriteRaw("""
        { "schemaVersion": 2, "columns": 4, "elements": [] }
        """);

        var loaded = Store().Load(TkId);

        Assert.Single(loaded.Errors);
        Assert.Equal("surface.file.unsupported-schema", loaded.Errors[0].Code);
    }

    [Fact]
    public void A_missing_file_is_not_a_defect()
    {
        // No file written at all. Both lists must be empty: an author who has not drawn a card is
        // not in the same situation as one whose file was rejected (ADR-0016 D4).
        var loaded = Store().Load(TkId);

        Assert.Empty(loaded.Errors);
        Assert.Empty(loaded.Warnings);
        Assert.Empty(loaded.Document.Elements);
    }

    [Fact]
    public void Unparsable_json_refuses_the_file_without_throwing()
    {
        WriteRaw("{ this is not json");

        var loaded = Store().Load(TkId);

        Assert.Single(loaded.Errors);
        Assert.Equal("surface.file.invalid-json", loaded.Errors[0].Code);
    }

    [Fact]
    public void Elements_missing_from_the_file_refuses_it()
    {
        WriteRaw("""{ "schemaVersion": 1, "columns": 4 }""");

        var loaded = Store().Load(TkId);

        Assert.Single(loaded.Errors);
        Assert.Equal("surface.file.elements-missing", loaded.Errors[0].Code);
    }

    // ── element tier ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_element_naming_an_undeclared_key_is_dropped_with_a_warning()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceTextElement { Key = "nope" }],
        };

        var result = SurfaceValidator.Validate(document, Keys());

        Assert.Empty(result.Effective.Elements);
        Assert.Single(result.Warnings);
        Assert.Equal(SurfaceDiagnosticCodes.ElementUnknownKey, result.Warnings[0].Code);
    }

    [Fact]
    public void An_input_that_neither_feeds_a_payload_nor_speaks_is_dropped()
    {
        // An editing element carries no key: the value travels the other way, under its name.
        // With neither a name nor any events it contributes nothing, so it is not drawable —
        // and `no-key` is the wrong reason, because it was never supposed to have one.
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceInputElement { Key = null }],
        };

        var result = SurfaceValidator.Validate(document, Keys(new KeyDeclaration("a", "string", KeyRole.Out)));

        Assert.Empty(result.Effective.Elements);
        Assert.Equal(SurfaceDiagnosticCodes.ElementInert, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void An_input_that_uploads_under_a_name_is_kept_even_with_no_key()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceInputElement { Name = "barcode" }],
        };

        var result = SurfaceValidator.Validate(document, Keys());

        Assert.Single(result.Effective.Elements);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void An_editing_element_that_declares_a_key_has_it_dropped_with_a_warning()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceInputElement { Key = "barcode", Name = "barcode" }],
        };

        var result = SurfaceValidator.Validate(
            document, Keys(new KeyDeclaration("barcode", "string", KeyRole.Out)));

        var kept = Assert.IsType<SurfaceInputElement>(Assert.Single(result.Effective.Elements));
        Assert.Null(kept.Key);
        Assert.Equal("barcode", kept.Name);
        Assert.Equal(SurfaceDiagnosticCodes.ElementKeyIgnored, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void A_button_reads_no_key_and_is_kept()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceButtonElement { Label = "go", Events = ["Click"] }],
        };

        var result = SurfaceValidator.Validate(document, Keys());

        Assert.Single(result.Effective.Elements);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_select_with_neither_option_source_is_dropped()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceSelectElement { Name = "mode" }],
        };

        var result = SurfaceValidator.Validate(
            document, Keys(new KeyDeclaration("mode", "string", KeyRole.Out)));

        Assert.Empty(result.Effective.Elements);
        // The element also declared `key`, so a `key-ignored` warning comes first. Look for the
        // code this test is about rather than assuming it is the only one.
        Assert.Contains(result.Warnings, w => w.Code == SurfaceDiagnosticCodes.SelectNoOptions);
    }

    [Fact]
    public void A_list_whose_key_is_not_declared_as_an_array_is_dropped()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceListElement { Key = "items" }],
        };

        var result = SurfaceValidator.Validate(
            document, Keys(new KeyDeclaration("items", "string", KeyRole.Out)));

        Assert.Empty(result.Effective.Elements);
        Assert.Equal(SurfaceDiagnosticCodes.ListKeyNotArray, result.Warnings[0].Code);
    }

    [Fact]
    public void A_list_declared_as_an_array_is_kept()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceListElement { Key = "items" }],
        };

        var result = SurfaceValidator.Validate(
            document, Keys(new KeyDeclaration("items", "array", KeyRole.Out)));

        Assert.Single(result.Effective.Elements);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_column_outside_the_grid_is_dropped()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceButtonElement { Label = "go", Column = 9 }],
        };

        var result = SurfaceValidator.Validate(document, Keys());

        Assert.Empty(result.Effective.Elements);
        Assert.Equal(SurfaceDiagnosticCodes.PositionOutOfGrid, result.Warnings[0].Code);
    }

    // ── field tier ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_span_wider_than_the_remaining_columns_is_clamped_not_dropped()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceButtonElement { Label = "go", Column = 3, ColumnSpan = 4 }],
        };

        var result = SurfaceValidator.Validate(document, Keys());

        var kept = Assert.IsType<SurfaceButtonElement>(Assert.Single(result.Effective.Elements));
        Assert.Equal(2, kept.ColumnSpan); // columns 3..4
        Assert.Equal(SurfaceDiagnosticCodes.SpanClamped, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void A_reversed_number_range_is_dropped_and_the_element_survives()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceNumberElement { Name = "n", Min = 10, Max = 1 }],
        };

        var result = SurfaceValidator.Validate(
            document, Keys(new KeyDeclaration("n", "number", KeyRole.Out)));

        var kept = Assert.IsType<SurfaceNumberElement>(Assert.Single(result.Effective.Elements));
        Assert.Null(kept.Min);
        Assert.Null(kept.Max);
        // Two warnings now: the element declared `key`, which an editing element may not have.
        Assert.Contains(result.Warnings, w => w.Code == SurfaceDiagnosticCodes.NumberRangeReversed);
    }

    [Fact]
    public void An_unknown_text_format_falls_back_to_plain_text()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceTextElement { Key = "t", Format = "holographic" }],
        };

        var result = SurfaceValidator.Validate(
            document, Keys(new KeyDeclaration("t", "string", KeyRole.Out)));

        var kept = Assert.IsType<SurfaceTextElement>(Assert.Single(result.Effective.Elements));
        Assert.Null(kept.Format);
        Assert.Equal(SurfaceDiagnosticCodes.TextUnknownFormat, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void Both_select_option_sources_mean_the_static_one_wins()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceSelectElement { Name = "mode", Options = ["a"], OptionsKey = "k" }],
        };

        var result = SurfaceValidator.Validate(
            document,
            Keys(
                new KeyDeclaration("mode", "string", KeyRole.Out),
                new KeyDeclaration("k", "array", KeyRole.Out)));

        var kept = Assert.IsType<SurfaceSelectElement>(Assert.Single(result.Effective.Elements));
        Assert.Null(kept.OptionsKey);
        Assert.Single(kept.Options);
        // Static wins, and the element's `key` was dropped too — assert the code, not the count.
        Assert.Contains(result.Warnings, w => w.Code == SurfaceDiagnosticCodes.SelectOptionsAmbiguous);
    }

    // ── change notification ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_raises_changed_once_with_the_toolkit_id()
    {
        var store = Store();
        var seen = new List<string>();
        store.Changed += (_, e) => seen.Add(e.ToolkitId);

        store.Save(TkId, new SurfaceDocument
        {
            Elements = [new SurfaceButtonElement { Label = "go" }],
        });

        Assert.Equal([TkId], seen);
    }

    [Fact]
    public void Delete_raises_changed_and_removes_the_file()
    {
        var store = Store();
        store.Save(TkId, SurfaceDocument.Empty);

        var seen = new List<string>();
        store.Changed += (_, e) => seen.Add(e.ToolkitId);

        store.Delete(TkId);

        Assert.Equal([TkId], seen);
        Assert.False(File.Exists(Path.Combine(_root.Root, TkId, SurfaceStore.FileName)));
    }

    [Fact]
    public void Save_then_load_round_trips_every_element_kind()
    {
        var store = Store();
        var original = new SurfaceDocument
        {
            Elements =
            [
                new SurfaceTextElement { Key = "t", Format = "date" },
                new SurfaceButtonElement { Label = "go", Events = ["Click", "Submit"] },
                new SurfaceInputElement { Key = "i" },
                new SurfaceNumberElement { Key = "n", Min = 0, Max = 10, Step = 2, Decimals = 1 },
                new SurfaceSelectElement { Key = "s", Options = ["a", "b"] },
                new SurfaceSwitchElement { Key = "b" },
                new SurfaceListElement { Key = "l", ItemTitle = "name" },
            ],
        };

        store.Save(TkId, original);
        var loaded = store.Load(TkId);

        Assert.Empty(loaded.Errors);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(7, loaded.Document.Elements.Count);
        Assert.Equal(
            original.Elements.Select(e => e.GetType()),
            loaded.Document.Elements.Select(e => e.GetType()));
    }
}
