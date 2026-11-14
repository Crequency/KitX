using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Data;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Surface;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Every diagnostic a surface emits must carry the <em>values</em> its sentence is made of, not
/// just the sentence (design §13.3).
/// </summary>
/// <remarks>
/// <para><b>Why <see cref="Diagnostic.Message"/> alone is not enough.</b> The producer runs in the
/// server assembly and has no language context, so the English sentence it builds cannot be the
/// text a Dutch or Chinese reader sees. The client is meant to look the wording up by
/// <see cref="Diagnostic.Code"/> and fill <c>{name}</c> placeholders from
/// <see cref="Diagnostic.Data"/>. A value missing from that dictionary is therefore not a cosmetic
/// gap — it is a hole in the sentence in every language except the fallback, and the fallback is
/// exactly what the reader was supposed to stop needing.</para>
///
/// <para><b>These tests pin values, not shapes.</b> Asserting that <c>Data</c> is non-null would
/// pass against an implementation that filled it with <c>kind</c> alone; each test below names the
/// entry it depends on, so deleting that one assignment from the producer turns this file red. Two
/// of them go further and pin which value was recorded where the producer had a choice: a repaired
/// weight must report the number that was wrong rather than the 1 it became, and a reversed range
/// must report both bounds rather than the fact that it was reversed.</para>
///
/// <para><b>They are written against the store and the validator</b>, the two places that produce
/// these diagnostics, so the wiring is exercised end to end — a <c>Data</c> dictionary built and
/// then dropped on the way to the result would fail here.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class SurfaceDiagnosticDataTests : IDisposable
{
    private const string TkId = "tk-surface-data";

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

    private static Diagnostic Single(SurfaceValidationResult result, string code)
        => Assert.Single(result.Warnings, warning => warning.Code == code);

    private static IReadOnlyDictionary<string, string> Data(Diagnostic diagnostic)
        => Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(diagnostic.Data);

    // ── the validator: the cell a sentence points at ───────────────────────────────────────────

    /// <summary>
    /// The three values every element diagnostic opens with, plus the one this code is about. A
    /// reader who only has the translation must still be able to find the cell: kind, row and
    /// weight are what the editor shows, and there is no id in a surface to point at.
    /// </summary>
    [Fact]
    public void An_element_diagnostic_names_the_cell_and_the_key_it_is_about()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceTextElement { Row = 3, Weight = 2, Key = "nobody-declared-this" }],
        };

        var data = Data(Single(SurfaceValidator.Validate(document, Keys()),
            SurfaceDiagnosticCodes.ElementUnknownKey));

        Assert.Equal("Text", data["kind"]);
        Assert.Equal("3", data["row"]);
        Assert.Equal("2", data["weight"]);
        Assert.Equal("nobody-declared-this", data["key"]);
    }

    /// <summary>
    /// The value that was wrong, not the one it was repaired to. The element keeps a weight of 1
    /// after this warning; reporting that would describe the repaired document rather than the
    /// defect, and the author would read "weight is 1, it is treated as 1" with nothing to fix.
    /// </summary>
    [Fact]
    public void A_repaired_weight_reports_the_value_that_was_wrong()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceTextElement { Row = 1, Weight = 0, Key = "title" }],
        };

        var data = Data(Single(
            SurfaceValidator.Validate(document, Keys(new KeyDeclaration("title", "string", KeyRole.Out))),
            SurfaceDiagnosticCodes.WeightRepaired));

        Assert.Equal("0", data["weight"]);
    }

    /// <summary>
    /// A translation has to say which way round the range was; "the range is reversed" without the
    /// two numbers would leave the author looking through the file for them.
    /// </summary>
    [Fact]
    public void A_reversed_range_reports_both_bounds()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceNumberElement { Row = 1, Name = "n", Min = 5, Max = 1 }],
        };

        var data = Data(Single(SurfaceValidator.Validate(document, Keys()),
            SurfaceDiagnosticCodes.NumberRangeReversed));

        Assert.Equal("5", data["min"]);
        Assert.Equal("1", data["max"]);
    }

    /// <summary>
    /// The declared type is what the author has to change, so it travels with the key rather than
    /// being left in the English sentence.
    /// </summary>
    [Fact]
    public void A_list_bound_to_a_non_array_reports_the_key_and_the_declared_type()
    {
        var document = new SurfaceDocument
        {
            Elements = [new SurfaceListElement { Row = 2, Key = "count" }],
        };

        var data = Data(Single(
            SurfaceValidator.Validate(document, Keys(new KeyDeclaration("count", "number", KeyRole.Out))),
            SurfaceDiagnosticCodes.ListKeyNotArray));

        Assert.Equal("count", data["key"]);
        Assert.Equal("number", data["type"]);
    }

    /// <summary>
    /// A sentence with no values in it carries <see langword="null"/>, not an empty dictionary:
    /// the client reads "nothing to substitute" from one meaning instead of having to recognise
    /// two spellings of it.
    /// </summary>
    /// <remarks>
    /// The only diagnostic in this file that takes no values is the element that deserialised to
    /// nothing — even a dropped element names its kind and cell, so those three are not a case of
    /// "no values" and this test would be wrong to use one of them. The button-with-no-label
    /// shortcut this started as is exactly that mistake: it asserts <c>kind</c>/<c>row</c>/<c>weight</c>
    /// are absent, which would mean the producer had stopped telling the reader where the problem is.
    /// </remarks>
    [Fact]
    public void A_sentence_with_no_values_carries_no_data()
    {
        WriteRaw("""
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [ null ]
        }
        """);

        var warning = Assert.Single(Store().Load(TkId).Warnings);
        Assert.Equal("surface.element.empty", warning.Code);
        Assert.Null(warning.Data);
    }

    /// <summary>
    /// Every element diagnostic carries the kind twice: the file's word and the name the client may
    /// replace it with.
    /// </summary>
    /// <remarks>
    /// <para><b>Why two entries rather than one.</b> <c>kind</c> is <c>"type": "Input"</c> — the word
    /// the author can search for in <c>surface.json</c> — and must never be translated. <c>kindName</c>
    /// is where the same element is called what the reader's language calls it, and this assembly
    /// cannot fill it: it has no dictionary, so it records the file word as the fallback and the
    /// client substitutes the translation. A sentence that printed only the first read half in
    /// English on a Chinese screen, which was the defect; one that printed only the second would
    /// leave the author nothing to search for.</para>
    ///
    /// <para><b>Both producers, because they are separate code.</b> The store builds its sentences
    /// while reading and the validator after compiling, and the store's two element-level codes were
    /// the ones that named the kind the other way round. Pinning only the validator would leave the
    /// pair that this change is actually about untested.</para>
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_element_diagnostic_carries_the_kind_under_the_name_the_file_and_the_reader_each_use(
        bool fromValidator)
    {
        Diagnostic warning;

        if (fromValidator)
        {
            var document = new SurfaceDocument
            {
                Elements = [new SurfaceTextElement { Row = 3, Weight = 2, Key = "nobody-declared-this" }],
            };

            warning = Single(SurfaceValidator.Validate(document, Keys()),
                SurfaceDiagnosticCodes.ElementUnknownKey);
        }
        else
        {
            WriteRaw("""
            {
              "schemaVersion": 1,
              "layoutVersion": 1,
              "elements": [
                { "type": "Text", "row": 1, "key": "title", "col": 2 }
              ]
            }
            """);

            warning = Assert.Single(Store().Load(TkId).Warnings);
            Assert.Equal(SurfaceDiagnosticCodes.ElementUnknownField, warning.Code);
        }

        var data = Data(warning);

        Assert.Equal("Text", data["kind"]);

        // The fallback is the file word, not an English name for the kind: this assembly knows one
        // word per kind and inventing a second here is exactly what would drift from the file.
        Assert.Equal(data["kind"], data["kindName"]);
    }

    /// <summary>
    /// Both of the store's element-level sentences, because it writes them on two different branches
    /// and only one of them was converted the day the other was.
    /// </summary>
    [Fact]
    public void The_store_names_the_kind_on_both_of_its_element_sentences()
    {
        WriteRaw("""
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Switch", "row": 1, "name": "copies", "events": ["changed"] },
            { "type": "Text", "row": 2, "key": "title", "col": 2 }
          ]
        }
        """);

        var warnings = Store().Load(TkId).Warnings;

        foreach (var code in new[]
                 {
                     SurfaceDiagnosticCodes.EventsNotAllowed,
                     SurfaceDiagnosticCodes.ElementUnknownField,
                 })
        {
            var warning = Assert.Single(warnings, w => w.Code == code);
            var data = Data(warning);

            Assert.True(data.ContainsKey("kindName"), $"'{code}' does not record kindName");
            Assert.Equal(data["kind"], data["kindName"]);
        }
    }

    // ── the store: the field or version the sentence names ─────────────────────────────────────

    /// <summary>
    /// A file-level warning names the field by the word the file uses. <c>columns</c> is the real
    /// case this check was written for (the retired column model), and it is the one a translation
    /// cannot invent: the author has to search the file for it.
    /// </summary>
    [Fact]
    public void An_unknown_top_level_field_names_the_field_and_the_file()
    {
        WriteRaw("""
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "columns": 4,
          "elements": []
        }
        """);

        var warning = Assert.Single(Store().Load(TkId).Warnings);
        Assert.Equal(SurfaceDiagnosticCodes.FileUnknownField, warning.Code);

        var data = Data(warning);
        Assert.Equal("columns", data["field"]);
        Assert.Equal(SurfaceStore.FileName, data["file"]);
    }

    /// <summary>
    /// Two fields are required, and the sentence is the same for both. Which one was absent is the
    /// whole content of the report, so it cannot live only in the English interpolation.
    /// </summary>
    [Fact]
    public void A_missing_version_names_which_field_was_missing()
    {
        WriteRaw("""
        {
          "schemaVersion": 1,
          "elements": []
        }
        """);

        var error = Assert.Single(Store().Load(TkId).Errors);
        Assert.Equal(SurfaceDiagnosticCodes.FileMissingVersion, error.Code);
        Assert.Equal("layoutVersion", Data(error)["field"]);
    }

    /// <summary>
    /// The element-level mirror of the test above: an unrecognised member names both the kind of
    /// element and the field nobody read, because the same field name is legal on another kind.
    /// </summary>
    [Fact]
    public void An_unknown_element_field_names_the_kind_and_the_field()
    {
        WriteRaw("""
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Text", "row": 1, "key": "title", "col": 2 }
          ]
        }
        """);

        var warning = Assert.Single(Store().Load(TkId).Warnings);
        Assert.Equal(SurfaceDiagnosticCodes.ElementUnknownField, warning.Code);

        var data = Data(warning);
        Assert.Equal("Text", data["kind"]);
        Assert.Equal("col", data["field"]);
    }
}
