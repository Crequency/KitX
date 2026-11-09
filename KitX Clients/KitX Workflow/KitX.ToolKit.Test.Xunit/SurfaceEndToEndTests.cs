using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KitX.ToolKit;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Run;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Surface;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.View;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// P7: one hand-written <c>surface.json</c> goes the whole way — file, validation, <c>welcome</c>,
/// and a minimal client that only has the message to work from.
/// </summary>
/// <remarks>
/// <para><b>The deliverable of this class is not passing tests.</b> It is the list of questions in
/// the comments marked <c>Q:</c> — places where the contract does not say enough for a renderer to
/// decide, so this client had to guess. A passing test here proves the guess was <i>consistent</i>,
/// not that it was <i>right</i>.</para>
///
/// <para><b>The file is written by hand on purpose.</b> Exporting it from the orchestrator would
/// produce exactly the layouts the orchestrator can express, which is the set that already works.
/// The interesting cases are the ones an author would type and the palette would never emit.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class SurfaceEndToEndTests : IDisposable
{
    private const string TkId = "scan-book";

    /// <summary>
    /// A scan-a-book card: barcode entry, title, cover, three buttons, and a session list.
    /// </summary>
    /// <remarks>
    /// Every element is placed by hand with a row and a weight, because that is all the format
    /// carries — there is no flow layout and no ordering rule. Within a row the order is the order
    /// the elements appear here, and the weights divide the row (Q101).
    /// </remarks>
    private const string SurfaceJson = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Input",  "name": "barcode", "row": 1, "weight": 4 },

            { "type": "Text",   "key": "title",   "row": 2, "weight": 3 },
            { "type": "Text",   "key": "cover",   "row": 2, "weight": 1 },

            { "type": "Button", "label": "Scan",     "events": ["scan"],     "row": 3, "weight": 1 },
            { "type": "Button", "label": "Add",      "events": ["add"],      "row": 3, "weight": 1 },
            { "type": "Button", "label": "Sessions", "events": ["sessions"], "row": 3, "weight": 2 },

            { "type": "List",   "key": "sessions", "row": 4, "weight": 4,
              "itemTitle": "startedAt", "itemSubtitle": "duration" }
          ]
        }
        """;

    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static Toolkit ToolkitWith(params KeyDeclaration[] keys) => new()
    {
        Id = TkId,
        Meta = new ToolkitMeta { Name = "scan-a-book" },
        Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
        Keys = [.. keys],
        Triggers =
        [
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
        ],
    };

    private static readonly KeyDeclaration[] CardKeys =
    [
        new("barcode", "string", KeyRole.Out),
        new("title", "string", KeyRole.Out),
        new("cover", "string", KeyRole.Out),
        new("sessions", "array", KeyRole.Stream),
        // Declared but never placed on the card — a renderer must not be told about it in `welcome`.
        new("scanCount", "number", KeyRole.Out),
    ];

    private string WriteSurface(string json)
    {
        var dir = Path.Combine(_root.Root, TkId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, SurfaceStore.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    // ── P7.2: the hand-written file survives validation with nothing to say ──────────────

    [Fact]
    public void The_hand_written_card_lands_without_warnings_and_without_errors()
    {
        WriteSurface(SurfaceJson);

        var store = new SurfaceStore(_root.Root);
        var loaded = store.Load(TkId);

        // File level: accepted, and the document that came back is the one on disk.
        Assert.Empty(loaded.Errors);
        Assert.Equal(SurfaceDocument.CurrentLayoutVersion, loaded.Document.LayoutVersion);
        Assert.Equal(7, loaded.Document.Elements.Count);

        // Element level: nothing is dropped. A warning here would mean the palette and the
        // validator disagree about what a reasonable card looks like.
        var table = KeyTable.From(TkId, CardKeys);
        var validated = SurfaceValidator.Validate(loaded.Document, table);

        Assert.Empty(validated.Warnings);
        Assert.Equal(7, validated.Effective.Elements.Count);
    }

    [Fact]
    public void A_single_mistyped_element_is_dropped_and_the_rest_survive()
    {
        // Pins §3.3.4.1: an unknown `type` is an element-level defect. If this ever returns Errors
        // instead, the JSON polymorphism has regressed to refusing the whole document — which is
        // what it did before the per-element parse.
        //
        // The replacement targets exactly one element (`title`'s line), not every `Text`: a blunt
        // Replace on `"type": "Text"` hits both Text elements and would assert 2 warnings while
        // looking like it tested one.
        WriteSurface(SurfaceJson.Replace(
            "{ \"type\": \"Text\",   \"key\": \"title\",",
            "{ \"type\": \"Caption\", \"key\": \"title\",",
            StringComparison.Ordinal));

        var store = new SurfaceStore(_root.Root);
        var loaded = store.Load(TkId);

        Assert.Empty(loaded.Errors);
        Assert.Single(loaded.Warnings);
        Assert.Equal(6, loaded.Document.Elements.Count);
    }

    [Fact]
    public void A_field_name_the_type_does_not_have_is_reported_instead_of_landing_in_a_default()
    {
        // The defect this pins: the design document wrote `col` for months while the property was
        // `Column`, and System.Text.Json put the misspelling nowhere without a word. The element
        // kept the default, so a whole card silently stacked in the first column.
        //
        // The element is still kept — it may well be drawable — but the author is told.
        WriteSurface(SurfaceJson.Replace(
            "\"row\": 2, \"weight\": 3",
            "\"row\": 2, \"wieght\": 3, \"wight\": 1",
            StringComparison.Ordinal));

        var store = new SurfaceStore(_root.Root);
        var loaded = store.Load(TkId);

        Assert.Empty(loaded.Errors);
        Assert.Equal(7, loaded.Document.Elements.Count);

        var misspelled = loaded.Warnings
            .Where(w => w.Code == SurfaceDiagnosticCodes.ElementUnknownField)
            .Select(w => w.Message)
            .ToList();

        // One line per stray field, not per element: both misspellings on that one element are
        // named, so an author fixing `wieght` does not discover `wight` on the next load.
        Assert.Equal(2, misspelled.Count);
        Assert.Contains(misspelled, m => m.Contains("'wieght'", StringComparison.Ordinal));
        Assert.Contains(misspelled, m => m.Contains("'wight'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_correctly_named_field_produces_no_warning_at_all()
    {
        // The other half: if the check fired on every element, the previous test would pass for
        // the wrong reason. This one fails if the known-member set is ever built too narrowly.
        WriteSurface(SurfaceJson);

        var loaded = new SurfaceStore(_root.Root).Load(TkId);

        Assert.DoesNotContain(
            loaded.Warnings,
            w => w.Code == SurfaceDiagnosticCodes.ElementUnknownField);
    }
    // ── P7.3: `welcome` is enough to draw the card ──────────────────────────────────────

    [Fact]
    public void Welcome_carries_the_surface_and_only_the_keys_that_have_values()
    {
        WriteSurface(SurfaceJson);

        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new SilentExecutor(),
            store,
            tk => new ToolkitFileStore(_root.Prepare(tk)));

        using (manager)
        {
            manager.Mount(ToolkitWith(CardKeys));

            // Three of the five declared keys have values. `cover` is declared and placed on the
            // card but has no value; `scanCount` is declared, has a value, and is not on the card.
            store.Set($"{TkId}/barcode", "978-0132350884");
            store.Set($"{TkId}/title", "Clean Code");
            store.Set($"{TkId}/scanCount", 3);

            var session = new ViewSession("s1", manager, store, new SurfaceStore(_root.Root));
            using (session)
            {
                var welcome = session.Attach(TkId);

                Assert.Equal(TkId, welcome.ToolkitId);
                Assert.Equal(7, welcome.Surface.Elements.Count);

                // Q: `welcome.values` contains only keys that have values, so a renderer cannot
                // tell "declared, no value yet" from "not declared at all". It has the card, so it
                // knows which keys the layout mentions — but it does not know the full declaration,
                // and therefore cannot show an empty field differently from an absent one.
                Assert.Equal(3, welcome.Values.Count);
                Assert.True(welcome.Values.ContainsKey("barcode"));
                Assert.True(welcome.Values.ContainsKey("title"));
                Assert.True(welcome.Values.ContainsKey("scanCount"));
                Assert.False(welcome.Values.ContainsKey("cover"));
                Assert.False(welcome.Values.ContainsKey("sessions"));
            }
        }
    }
    /// <summary>Runs nothing: this suite is about what the store produces, not what a body does.</summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}