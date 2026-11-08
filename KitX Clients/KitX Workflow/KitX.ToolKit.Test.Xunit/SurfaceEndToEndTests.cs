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
    /// Every element is placed by hand with a row and a column, because that is all the format
    /// carries — there is no flow layout and no ordering rule.
    /// </remarks>
    private const string SurfaceJson = """
        {
          "schemaVersion": 1,
          "columns": 4,
          "elements": [
            { "type": "Input",  "key": "barcode", "row": 1, "column": 1, "columnSpan": 4 },

            { "type": "Text",   "key": "title",   "row": 2, "column": 1, "columnSpan": 3 },
            { "type": "Text",   "key": "cover",   "row": 2, "column": 4, "columnSpan": 1 },

            { "type": "Button", "label": "Scan",     "events": ["scan"],     "row": 3, "column": 1, "columnSpan": 1 },
            { "type": "Button", "label": "Add",      "events": ["add"],      "row": 3, "column": 2, "columnSpan": 1 },
            { "type": "Button", "label": "Sessions", "events": ["sessions"], "row": 3, "column": 3, "columnSpan": 2 },

            { "type": "List",   "key": "sessions", "row": 4, "column": 1, "columnSpan": 4,
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
        Assert.Equal(4, loaded.Document.Columns);
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
        // The defect this pins: the design document wrote `col` for months, the property is
        // `Column`, and System.Text.Json put the misspelling nowhere without a word. The element
        // kept Column = 1, so a whole card silently stacked in the first column.
        //
        // The element is still kept — it may well be drawable — but the author is told.
        WriteSurface(SurfaceJson.Replace(
            "\"row\": 2, \"column\": 1, \"columnSpan\": 3",
            "\"row\": 2, \"col\": 1, \"colSpan\": 3",
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
        // named, so an author fixing `col` does not discover `colSpan` on the next load.
        Assert.Equal(2, misspelled.Count);
        Assert.Contains(misspelled, m => m.Contains("'col'", StringComparison.Ordinal));
        Assert.Contains(misspelled, m => m.Contains("'colSpan'", StringComparison.Ordinal));
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

    // ── P7.4: the minimal client, and what it had to guess ─────────────────────────────

    /// <summary>One element, positioned from the message alone.</summary>
    private sealed record Placed(string Kind, string? Key, int Row, int Column, int Span)
    {
        public int LastColumn => Column + Span - 1;
    }

    /// <summary>
    /// A client that has only a <see cref="WelcomeMessage"/>.
    /// </summary>
    /// <remarks>
    /// It computes position and nothing else — exactly the information the message carries. Every
    /// <c>Q:</c> below is a question the contract did not answer, so the code had to pick.
    /// </remarks>
    private static List<Placed> Place(WelcomeMessage welcome)
    {
        var placed = new List<Placed>();
        foreach (var element in welcome.Surface.Elements)
        {
            // Q1: nothing says elements are sorted, unique per cell, or non-overlapping. This
            // client trusts `row`/`column` as-is; two elements claiming one cell would both be
            // drawn, on top of each other, with nothing in the contract saying which wins.
            placed.Add(new Placed(
                element.GetType().Name.Replace("Surface", string.Empty).Replace("Element", string.Empty),
                element.Key,
                element.Row,
                element.Column,
                element.ColumnSpan));
        }

        return placed;
    }

    [Fact]
    public void The_minimal_client_can_place_every_element_without_running_off_the_grid()
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

            var session = new ViewSession("s1", manager, store, new SurfaceStore(_root.Root));
            using (session)
            {
                var welcome = session.Attach(TkId);
                var placed = Place(welcome);

                Assert.Equal(7, placed.Count);

                foreach (var element in placed)
                {
                    Assert.InRange(element.Row, 1, 7);
                    Assert.InRange(element.Column, 1, welcome.Surface.Columns);
                    Assert.InRange(element.LastColumn, 1, welcome.Surface.Columns);
                }

                // The three buttons sit on one row, in order.
                var buttons = placed.Where(p => p.Kind == "Button").ToList();
                Assert.Equal(3, buttons.Count);
                Assert.All(buttons, b => Assert.Equal(3, b.Row));
                Assert.Equal([1, 2, 3], buttons.Select(b => b.Column).ToArray());

                // Q2: `row` is a row index, not a y coordinate — so nothing in the message says how
                // tall a row is. A client that has never rendered this card cannot compute pixel
                // positions from the message alone; it has to invent a row height or lay out in
                // grid units and let the control do the arithmetic.
                Assert.All(placed, p => Assert.True(p.Row >= 1));
            }
        }
    }

    [Fact]
    public void Widening_the_list_does_not_move_it_or_anything_else()
    {
        // §10.1.2's checkable consequence: `colSpan` is a width parameter, not a re-layout.
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
            var surfaces = new SurfaceStore(_root.Root);

            List<Placed> Render()
            {
                var session = new ViewSession("s1", manager, store, surfaces);
                using (session)
                    return Place(session.Attach(TkId));
            }

            var narrow = Render();
            var narrowList = Assert.Single(narrow, p => p.Kind == "List");

            // Rewrite the list at span 1 and re-read. Everything else must be unchanged.
            WriteSurface(SurfaceJson.Replace(
                "\"type\": \"List\",   \"key\": \"sessions\", \"row\": 4, \"column\": 1, \"columnSpan\": 4",
                "\"type\": \"List\",   \"key\": \"sessions\", \"row\": 4, \"column\": 1, \"columnSpan\": 1",
                StringComparison.Ordinal));

            var wide = Render();
            var wideList = Assert.Single(wide, p => p.Kind == "List");

            Assert.Equal(narrowList.Row, wideList.Row);
            Assert.Equal(narrowList.Column, wideList.Column);
            Assert.Equal(4, narrowList.Span);
            Assert.Equal(1, wideList.Span);

            // Every other element is untouched: widening a cell is a width change, not a reflow.
            Assert.Equal(
                narrow.Where(p => p.Kind != "List").Select(p => (p.Kind, p.Row, p.Column, p.Span)),
                wide.Where(p => p.Kind != "List").Select(p => (p.Kind, p.Row, p.Column, p.Span)));
        }
    }

    /// <summary>
    /// A workflow executor that does nothing — these tests exercise the card pipeline, not a run.
    /// </summary>
    private sealed class SilentExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
