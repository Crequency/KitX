using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// Step one of the renderer: a card lays out its rows at the right distances.
/// </summary>
/// <remarks>
/// <para><b>What these tests are for.</b> P8 proved what the positions <em>should</em> be, in a pure
/// function, with no screen involved. What it could not prove is that anything draws them there. A
/// renderer can compute correctly and paint wrongly, and until this file existed no test could tell
/// the difference.</para>
///
/// <para><b>Why the two sides of the comparison are independent.</b> The expected values come from
/// <c>KitX.ToolKit.View.SurfaceLayout</c>; the actual values come from Avalonia's layout system via
/// <see cref="SurfaceCard"/>. The card deliberately does not call <c>SurfaceLayout</c> — it maps an
/// element kind to a pixel height on its own. If it reused the layout functions, a wrong slot height
/// would be wrong on both sides and cancel out, and every assertion here would be green while the
/// card was drawn incorrectly.</para>
///
/// <para><b>Top differences, not absolute pixels.</b> A card may carry margins or a header; none of
/// that is the renderer's opinion about vertical position. Differences are unaffected by such
/// padding, and "which row is above which, and how far apart" is the entire content of the rule.</para>
///
/// <para><b>If a Top difference comes out slightly wrong, do not widen the tolerance.</b> Avalonia
/// round layout by default, and a rounded <c>Grid</c> measures a 64px row as 65 — every row, so
/// differences read 65 where 64 was declared. Widening the tolerance to 1.5 makes the test pass on
/// one slot height; at 63.5 the same tolerance sits exactly on the boundary, and at ten rows the
/// error has grown to ten pixels. The tolerance is not what is broken. Ask why the number is not
/// what was declared; <see cref="SurfaceCard"/> answers that by turning rounding off.</para>
/// </remarks>
public sealed class SurfaceCardLayoutTests
{
    // ── The environment, before any geometry ─────────────────────────────────────────────

    [AvaloniaFact]
    public void The_layout_system_actually_runs_here()
    {
        // Every assertion in this file would pass against a card whose children all had zero size,
        // because zero minus zero is zero. So the first thing established is that the environment
        // is producing real numbers.
        //
        // This is not ceremony: a missing FluentTheme leaves controls with no template, and an
        // untemplated control measures to zero without any error anywhere. The failure mode is a
        // suite that passes and a renderer that draws nothing.
        var card = Card(new SurfaceTextElement { Row = 1, Key = "title" });

        var child = CellAt(card, 0);

        Assert.True(card.Bounds.Width > 0, $"card width is {card.Bounds.Width}");
        Assert.True(card.Bounds.Height > 0, $"card height is {card.Bounds.Height}");
        Assert.True(child.Bounds.Height > 0, $"child height is {child.Bounds.Height}");
    }

    // ── The criterion ────────────────────────────────────────────────────────────────────

    // ── Criterion 0: the card asks for what it draws ─────────────────────────────────────

    /// <summary>
    /// Twelve row numbers with a six-slot <c>List</c> at row six — 17 slots, 1088px of content.
    /// </summary>
    /// <remarks>
    /// A document nothing forbids, and the first one in this file taller than a screen. Every other
    /// fixture here fits, which is exactly why the card could claim 845 while painting to 1088 for
    /// as long as it did: with every document short, "asks for" and "draws" agree.
    ///
    /// <b>17 slots, not 13.</b> Eleven ordinary rows contribute one slot each; row six is the List,
    /// which contributes six. The first version of this fixture and its assertions both said 13 —
    /// eleven rows plus a List is 11 + 6 = 17, and 13 was a number arrived at by counting rows
    /// rather than adding them up. The two matched each other, so nothing went red; the assertion
    /// and the fixture were wrong in the same direction, which is the failure a self-consistent
    /// test cannot catch on its own.
    /// </remarks>
    private static List<SurfaceElement> TallerThanTheCard()
    {
        var elements = new List<SurfaceElement>();
        for (var row = 1; row <= 12; row++)
            elements.Add(new SurfaceTextElement { Row = row, Key = $"row{row}" });

        elements.Add(new SurfaceListElement { Row = 6, Key = "sessions" });
        return elements;
    }

    /// <summary>What <see cref="TallerThanTheCard"/> adds up to: eleven one-slot rows and a List.</summary>
    private static int TallerThanTheCardSlots => 11 + ListSlots;

    [AvaloniaFact]
    public void A_card_taller_than_a_screen_asks_for_every_pixel_it_draws()
    {
        // The defect this guards is not "content overflows". It is "the card says 845 and paints
        // 1088", and the three things that sized themselves from the card all believed the 845: the
        // host's ScrollViewer reported an extent of 845 over 1088 of content, the editor's canvas
        // drew an 845-high border around cells whose tops reach 1024, and the preview window opened
        // at CardHeight + 96. So the bottom 243px of a tall document was reachable by no window.
        var card = Unconstrained(Set(TallerThanTheCard()));

        // Derived from the fixture's own structure rather than written as a product: 13 slots was a
        // plausible-looking number that happened to equal the row count.
        var expected = TallerThanTheCardSlots * SurfaceMetrics.SlotHeight;

        Assert.Equal(expected, card.DesiredSize.Height, tolerance: 0.5);

        // And the rows are laid out there, not merely required there — a control can report a
        // DesiredSize it then fails to arrange into.
        //
        // Summed over the row grids rather than taken as the lowest control's bottom edge. A control
        // is FieldHeight tall and sits FieldInset below its row's top, so the bottom of the last
        // control is (SlotHeight - FieldHeight) short of the bottom of the card: on this fixture that
        // reads 1072 against a card of 1088, and the sixteen pixels are the inset, not a defect.
        Assert.Equal(
            expected,
            CardProbe.Rows(card).Sum(row => row.Bounds.Height),
            tolerance: 0.5);

        // Nothing was achieved by clipping. A card that hid the extra rows would satisfy "no pixel
        // is drawn outside the card" and lose the author's work silently, which is the failure the
        // old build had in a different form.
        Assert.False(card.ClipToBounds, "the card must grow, not clip");
    }

    [AvaloniaFact]
    public void A_card_shorter_than_a_screen_still_fills_one()
    {
        // The other direction, and it is not symmetry for its own sake: the confirmation scrim spans
        // the card's rows, so a one-element document must still produce a full-height card or the
        // dialog stops being modal over the empty part.
        var card = Unconstrained(Set([new SurfaceTextElement { Row = 1, Key = "title" }]));

        Assert.Equal(SurfaceMetrics.CardHeight, card.DesiredSize.Height, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_list_makes_the_card_ask_for_the_slots_it_fills()
    {
        // A List on row 1 with a footer on row 2 is (ListSlots + 1) slots of content — 448px, which
        // is *less* than one screen. So the card reports CardHeight, and the useful reading is the
        // one that distinguishes the two possible reasons for it: the content was measured and found
        // shorter than the floor, or the content was never measured at all.
        //
        // Hence the tall document alongside it. A card that ignores its content and always says 845
        // passes the short assertion below for the wrong reason; it cannot pass the tall one.
        var shortCard = Unconstrained(Set([new SurfaceListElement { Row = 1, Key = "sessions" },
                                           new SurfaceTextElement { Row = 2, Key = "footer" }]));

        Assert.Equal(
            SurfaceMetrics.CardHeight,
            shortCard.DesiredSize.Height,
            tolerance: 0.5);
        Assert.True(
            (1 + ListSlots) * SurfaceMetrics.SlotHeight < SurfaceMetrics.CardHeight,
            "this test's point is that the content is shorter than the floor; if ListSlots grows past "
            + "that, the assertion above stops being about the floor");

        // The same List in a document that does exceed the floor: now the reported height must be
        // the content's, which is the half that fails on a card with a fixed Height.
        var tallCard = Unconstrained(Set(TallerThanTheCard()));

        Assert.Equal(
            TallerThanTheCardSlots * SurfaceMetrics.SlotHeight,
            tallCard.DesiredSize.Height,
            tolerance: 0.5);
    }

    [AvaloniaFact]
    public void Two_elements_in_consecutive_rows_are_one_slot_height_apart()
    {
        // The simplest case the criterion covers, and the one a renderer gets wrong first: rows are
        // stacked tightly, not spread to fill the card.
        var elements = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Key = "first" },
            new SurfaceTextElement { Row = 2, Key = "second" },
        };

        var card = Card([.. elements]);
        var actual = TopAt(card, 1) - TopAt(card, 0);

        var expected = SurfaceLayout.PixelTopOf(elements, elements[1], SlotHeight, ListSlots)
                     - SurfaceLayout.PixelTopOf(elements, elements[0], SlotHeight, ListSlots);

        Assert.Equal(expected, actual, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_list_makes_the_row_below_it_sit_exactly_its_extra_slots_lower()
    {
        // The synthetic input, in pixels rather than slots: all seven real element kinds are one slot
        // tall, so a renderer that ignored the List's extra slots would pass every other assertion
        // here.
        //
        // <b>Two cards, and both sides of the comparison are differences.</b> An earlier version read
        // the absolute Top of the element below the list and compared it against a difference; that
        // only held because the first slot happens to start at zero. Comparing "the same element on
        // two cards" drops that assumption and is strictly stronger — the left side comes from
        // SurfaceMetrics and the right side from the visual tree, so a wrong ListSlotCount on either
        // side cannot cancel out.
        //
        // <b>What this measures is slot height, not what a List looks like.</b> At this stage the
        // card draws only Text, so a List contributes its row height and no control. A later reader
        // who assumes this covers List rendering would delete it while writing List — it does not,
        // and it must not be deleted then, because that row height is what makes a List occupy six
        // slots of space.
        var withList = Card(
            new SurfaceListElement { Row = 1, Key = "sessions" },
            new SurfaceTextElement { Row = 2, Key = "below" });

        var withText = Card(
            new SurfaceTextElement { Row = 1, Key = "sessions" },
            new SurfaceTextElement { Row = 2, Key = "below" });

        var extra = TopAt(withList, 1) - TopAt(withText, 1);
        var expected = (SurfaceMetrics.ListSlotCount - 1) * SurfaceMetrics.SlotHeight;

        Assert.Equal(expected, extra, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void Removing_a_value_does_not_move_anything_on_the_card()
    {
        // §10.2.1.1: a value decides what a cell shows, not whether the cell is there.
        //
        // Text is the only element that can exercise this today — it binds a key, and
        // `welcome.values` may or may not contain that key. Button is the opposite sample: it never
        // binds a key, so "has a value" is not a state it can be in, and it cannot demonstrate this
        // rule at all. (Both still occupy a row; the two facts are separate.)
        //
        // The two cards must actually differ, or this proves nothing. An earlier version built both
        // from the same document with no SetValue on either side and compared Top *differences*: it
        // pitted one implementation against itself, so it passed under every renderer — including one
        // that hides empty cells, which is exactly what its own comment claimed it would catch.
        // Absolute positions from two genuinely different cards is what a hiding renderer fails on:
        // it collapses the three empty cells and every Top below them shifts up.
        var elements = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Key = "title" },
            new SurfaceTextElement { Row = 2, Key = "subtitle" },
            new SurfaceTextElement { Row = 3, Key = "note" },
        };

        var withValues = Card(elements, new Dictionary<string, string>
        {
            ["title"] = "\"T\"",
            ["subtitle"] = "\"S\"",
            ["note"] = "\"N\"",
        });

        var withoutValues = Card([.. elements]);

        Assert.Equal(TopAt(withValues, 0), TopAt(withoutValues, 0), tolerance: 0.5);
        Assert.Equal(TopAt(withValues, 1), TopAt(withoutValues, 1), tolerance: 0.5);
        Assert.Equal(TopAt(withValues, 2), TopAt(withoutValues, 2), tolerance: 0.5);

        // And the cells are still there, each with a control in it — not collapsed to nothing.
        // Since Q100 the control is FieldHeight and the cell it sits in is still a full slot, so
        // the assertion that "the cell survived" is about the row, not about the control's height.
        Assert.Equal(
            SurfaceMetrics.FieldHeight,
            CellAt(withoutValues, 0).Bounds.Height,
            tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_gap_in_the_row_numbers_leaves_no_gap_on_the_card()
    {
        // §10.2.1.2: row is an ordinal, not a coordinate. Rows 1 and 5 must look exactly like rows
        // 1 and 2 — the pixels are where "the file numbers rows, it does not space them" stops being
        // a claim about the layout function and becomes a claim about the card.
        var adjacent = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Key = "a" },
            new SurfaceTextElement { Row = 2, Key = "b" },
        };

        var withGap = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Key = "a" },
            new SurfaceTextElement { Row = 5, Key = "b" },
        };

        var adjacentCard = Card([.. adjacent]);
        var gapCard = Card([.. withGap]);

        var adjacentDelta = TopAt(adjacentCard, 1) - TopAt(adjacentCard, 0);
        var gapDelta = TopAt(gapCard, 1) - TopAt(gapCard, 0);

        Assert.Equal(adjacentDelta, gapDelta, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void The_four_editable_kinds_each_occupy_exactly_one_slot()
    {
        // Four kinds in four rows, every one of them empty — no value has been typed and none can be,
        // since this stage builds them blank on purpose.
        //
        // That emptiness is the point. An empty field and a filled field are the same height, because
        // a field owns its slot the way any other element does: the value decides what the cell shows,
        // not whether the cell exists (§10.2.1.1). A renderer that sized a text box to its contents
        // would collapse four rows onto one another here, and would do it in every real card too.
        //
        // Written without naming a single element. That is not shorthand: a layout assertion that
        // has to know which element it is looking at is testing the wiring of names, not the layout.
        // Naming them also checks less — the four named cells could be one slot tall while a fifth
        // element was not, and this version would miss it the other way round: it walks every child
        // the card added, so an extra one cannot hide.
        var elements = new List<SurfaceElement>
        {
            new SurfaceInputElement { Row = 1 },
            new SurfaceNumberElement { Row = 2 },
            new SurfaceSelectElement { Row = 3 },
            new SurfaceSwitchElement { Row = 4 },
        };

        var card = Card([.. elements]);

        var cells = CardProbe.Elements(card);

        Assert.Equal(4, cells.Count);

        // Two quantities, and Q100 made them differ: the slot is 64, the control in it is 32.
        // Asserting that both equal SlotHeight was true before Q100 and had to stop being true —
        // it is exactly the assertion that would have hidden a control shrinking to 32 while the
        // row stayed 64, which is what the verdict now asks for on purpose.
        foreach (var cell in cells)
            Assert.Equal(SurfaceMetrics.FieldHeight, cell.Bounds.Height, tolerance: 0.5);

        // The rows themselves are still one slot apart — that half is unchanged by Q100. The tops
        // come from CardProbe because since Q101 each row is its own Grid: a raw Bounds.Top is
        // measured from the top of its row and would read 0 for all four.
        var tops = cells.Select(c => CardProbe.TopOf(card, c)).ToList();
        foreach (var (a, b) in tops.Zip(tops.Skip(1)))
            Assert.Equal(SurfaceMetrics.SlotHeight, b - a, tolerance: 0.5);

        // The other half of the verdict: the control sits (SlotHeight - FieldHeight) / 2 below its
        // row's top, for every element. On four single-slot rows this does not separate "centred in
        // the row" from "inset from the row's top" — those two agree at 16 when the row is 64. The
        // test below is where they diverge; this one is here so the four-kind case is covered too.
        foreach (var cell in cells)
            Assert.Equal(SurfaceMetrics.FieldInset, CardProbe.TopOf(card, cell) - CardProbe.RowTopOf(card, cell), 0.5);
    }

    [AvaloniaFact]
    public void A_button_beside_a_tall_row_sits_at_the_top_of_its_row_not_in_the_middle_of_it()
    {
        // The verdict for Q100 reads "(SlotHeight - FieldHeight) / 2 below its row's top", and the
        // design note says "VerticalAlignment = Center". Those two agree on a one-slot row and
        // disagree on a taller one: centring a 32px control in a row that grew to K slots puts it at
        // the row's middle, while an inset from the top puts it 16px below the row's start.
        //
        // A button sharing a row with a six-slot list is the smallest card where the two readings
        // differ — 208 against 368 on this geometry. Asserting on a one-slot card cannot tell them
        // apart, which is why this fixture exists rather than one more assertion above.
        //
        // The button comes second in the array because since Q101 that is the only thing that says
        // "to the right of the list": there is no column index any more, and a row divides its width
        // among its elements in the order the file lists them.
        var list = new SurfaceListElement { Row = 1, Key = "sessions", Weight = 3 };
        var button = new SurfaceButtonElement { Row = 1, Label = "Add", Events = ["Add"] };

        var card = Card([list, button]);

        var listView = Scroller(card);
        var buttonView = card.GetVisualDescendants().OfType<Button>().Single();

        // The row is six slots tall, and its top is the card's top — no element above it. Both
        // elements are in Grid row 0, so the row's own height is what says "six slots", not a
        // difference between two elements' tops.
        Assert.Equal(ListSlots * SurfaceMetrics.SlotHeight, card.RowDefinitions[0].ActualHeight, 0.5);

        // Both elements start the card, so their row top is zero.
        Assert.Equal(0.0, RowTopAt(card, 0), tolerance: 0.5);

        // Insets from its row's top. Centring in a 384px row would put it at 192 + 176 = 368.
        Assert.Equal(
            SurfaceMetrics.FieldInset,
            buttonView.Bounds.Top - RowTopAt(card, 0),
            tolerance: 0.5);

        // The list's control, on the other hand, is still one slot tall — §10.2.1.0's other half.
        Assert.Equal(SurfaceMetrics.SlotHeight, listView.Bounds.Height, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_control_without_a_key_still_occupies_its_slot()
    {
        // SurfaceElement.Key is nullable: an element may read nothing and still be a thing on the
        // card. Skipping it would make a row's height depend on whether a key was written — the same
        // failure as letting a value decide the layout, arriving from the other side.
        //
        // The sample is a Button, not an Input. Since Q71 an editing element may not carry a key at
        // all — the validator strips it and warns — so an Input with Key = null would satisfy this
        // test by construction and prove nothing. A Button never has one, and still has to take up
        // its slot.
        var elements = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Key = "title" },
            new SurfaceButtonElement { Row = 2, Label = "Save" },
            new SurfaceTextElement { Row = 3, Key = "footer" },
        };

        var card = Card([.. elements]);

        // Two slot-heights from title to footer: the keyless input still takes its row.
        Assert.Equal(
            2 * SurfaceMetrics.SlotHeight,
            TopAt(card, 2) - TopAt(card, 0),
            tolerance: 0.5);

        Assert.Equal(3, CardProbe.Elements(card).Count);
    }

    [AvaloniaFact]
    public void A_list_control_is_one_slot_tall_even_though_its_row_is_six()
    {
        // §10.2.1.0: "how tall the element is painted" and "how tall the slot it stands in grows" are
        // two quantities, and only the second is K.
        //
        // This is the assertion that keeps the two apart, and it is the one that would look correct
        // before this stage existed — with only Text on the card both heights are one slot, so a
        // renderer that painted a list K tall passes everything else in this file. It stops looking
        // correct the moment a scrollbar has to sit on the control's boundary rather than the cell's.
        var listSlot = new SurfaceListElement { Row = 1, Key = "sessions" };
        var below = new SurfaceTextElement { Row = 2, Key = "footer" };

        var card = Card([listSlot, below]);

        // The row grew by K slots — that half already holds, and it is read from the row structure
        // rather than from the control's edge, so it is not an identity.
        Assert.Equal(
            ListSlots * SurfaceMetrics.SlotHeight,
            RowTopAt(card, 1),
            tolerance: 0.5);

        // The control did not grow with it.
        Assert.Equal(
            SurfaceMetrics.SlotHeight,
            Scroller(card).Bounds.Height,
            tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_list_with_more_entries_than_fit_scrolls_inside_its_own_control()
    {
        // The overflow has to be absorbed by the control, not by the row: nothing below may move, and
        // the scroll area must actually be taller than what it shows.
        //
        // The entries now arrive the way the protocol delivers them — a value under the list's own
        // key — rather than as a render argument. The earlier version passed them in, which meant the
        // caller had to decide which of its values belonged to this control; the key is the same key
        // either way, so reading the array out of it is a lookup rather than a decision.
        var entries = Enumerable.Range(1, 40).Select(i => $"row {i}").ToArray();

        SurfaceCard TwoRows(int count) => Card(
            [new SurfaceListElement { Row = 1, Key = "sessions" },
             new SurfaceTextElement { Row = 2, Key = "footer" }],
            values: new Dictionary<string, string>
            {
                ["sessions"] = JsonSerializer.Serialize(entries.Take(count)),
            });

        var few = TwoRows(2);
        var many = TwoRows(40);

        // Forty entries move nothing that the layout placed.
        Assert.Equal(TopAt(few, 1), TopAt(many, 1), tolerance: 0.5);

        // And the two lists are still the same height, because the growth went into scrolling.
        Assert.Equal(
            Scroller(few).Bounds.Height,
            Scroller(many).Bounds.Height,
            tolerance: 0.5);

        var scroller = Scroller(many);

        Assert.True(
            scroller.Extent.Height > scroller.Viewport.Height,
            $"expected the list to overflow its control: extent {scroller.Extent.Height}, "
            + $"viewport {scroller.Viewport.Height}");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────
    /// <summary>What this renderer says a slot is worth, taken from the renderer and not restated.</summary>
    private const int SlotHeight = (int)SurfaceMetrics.SlotHeight;

    /// <summary>What this renderer says a <c>List</c> occupies.</summary>
    private const int ListSlots = SurfaceMetrics.ListSlotCount;

    /// <summary>
    /// Builds the card and runs a real layout pass over it.
    /// </summary>
    /// <remarks>
    /// <b>The explicit Measure/Arrange is not a workaround for a bug.</b> There is no window manager
    /// in a headless session, so nothing would ever ask the card how big it is: a Window constructed
    /// with Width 390 still reports Bounds of 1024×768 and only its DesiredSize reflects the
    /// request. Running the pass by hand is what a window would otherwise do, and it has to happen
    /// before any Bounds can be read.
    /// </remarks>
    private static SurfaceCard Card(params SurfaceElement[] elements) =>
        Card(elements, values: null);

    /// <inheritdoc cref="Card(SurfaceElement[])"/>
    private static SurfaceCard Card(
        IReadOnlyList<SurfaceElement> elements,
        IReadOnlyDictionary<string, string>? values)
    {
        var card = new SurfaceCard();
        card.SetDocument(new SurfaceDocument { Elements = elements });

        // Values go through the same door the protocol uses. The card no longer takes them as a
        // render argument, because there is no such argument on the wire either: a surface message
        // carries a document and nothing else.
        //
        // Parsed rather than SerializeToElement'd: these fixtures are written as JSON text because
        // that is what a list value is, and serialising the string would hand the card a JSON string
        // instead of a JSON array — which reads as "the list has one entry whose text is [\"row 1\"]".
        foreach (var (key, value) in values ?? new Dictionary<string, string>())
            card.SetValue(key, JsonDocument.Parse(value).RootElement.Clone());

        // Re-hosted in a Window because some controls only exist after templating: a ScrollViewer is
        // built from a control template, the template comes from the theme, and the theme is reached
        // through the visual root — with no root the template never instantiates and the content is
        // never measured. That reads as "this list does not overflow" rather than as "this list has
        // no template".
        //
        // <b>One window for the whole class, not one per card.</b> Each Show() leaves a window
        // holding native resources and stacking above the previous one; nine of them in a run is
        // invisible until a test starts failing for a reason unrelated to what it asserts. Replacing
        // the content of a single host costs one line and removes the question.
        var window = Host;
        window.Content = card;

        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        return card;
    }

    /// <summary>The single window every card is laid out inside.</summary>
    /// <remarks>
    /// <para>Built once, lazily, and never closed: an <c>AvaloniaFact</c> runs on the platform thread
    /// and the window outlives any one test. Its size is the card's, so a card's DesiredSize and its
    /// arranged bounds coincide — reading Bounds off a control is only meaningful when the surface it
    /// was arranged into is the size the layout rules assume.</para>
    ///
    /// <para><b><c>Show()</c> is what makes the templates exist.</b> A window that was measured but
    /// never shown has no visual root, so a control template is never instantiated and a templated
    /// control measures to zero — the failure the whole hosting arrangement exists to avoid. Showing
    /// it once at construction is the single line that supplies the root; the card underneath is
    /// swapped per test.</para>
    /// </remarks>
    private static Window Host
    {
        get
        {
            if (_host is not null)
                return _host;

            _host = new Window
            {
                Width = SurfaceMetrics.CardWidth,
                Height = SurfaceMetrics.CardHeight,
            };

            _host.Show();

            return _host;
        }
    }

    private static Window? _host;

    /// <summary>
    /// The n-th control the card added, top edge first — the card's own order, not a name.
    /// </summary>
    /// <remarks>
    /// <b>Why positional, and why that is not the compromise it looks like.</b> In the product,
    /// pointing at an element by position is wrong: the author moves a row and every reference goes
    /// quietly stale, which is exactly why Q80 deleted the element <c>id</c>. In a test the fixture
    /// and the assertion sit in one file and change together, so there is no "somebody reordered it
    /// and I did not notice". The dangerous version of positional addressing is the one that crosses
    /// a boundary; this one does not.
    /// </remarks>
    private static Control CellAt(SurfaceCard card, int index) => CardProbe.ElementAt(card, index);

    /// <summary>A card with a document, before anything has measured it.</summary>
    private static SurfaceCard Set(IReadOnlyList<SurfaceElement> elements)
    {
        var card = new SurfaceCard();
        card.SetDocument(new SurfaceDocument { Elements = elements });
        return card;
    }

    /// <summary>
    /// Runs a layout pass in a host tall enough that the window does not decide the card's height.
    /// </summary>
    /// <remarks>
    /// <para><b>The height here is deliberately not <see cref="SurfaceMetrics.CardHeight"/>.</b>
    /// Measuring a control inside a window of a given height measures the window's opinion, not the
    /// control's: with a 390x845 window, both the tall and the short fixture reported a
    /// <c>DesiredSize</c> of 768 — the window's client area, in neither case the card's own answer.
    /// A test for "how tall does the card ask to be" has to remove the constraint that would answer
    /// for it.</para>
    ///
    /// <para><b>The arrange rectangle comes from <c>DesiredSize</c>, not from a second constant.</b>
    /// Any fixed height here — 2000 as much as 845 — is a constraint that starts truncating the
    /// moment a document is taller than it, and then the test is green about a card whose bottom
    /// rows were never laid out. Taking the height the card just asked for keeps the upper bound
    /// belonging to the thing under test. This does not make the assertion self-fulfilling: what is
    /// asserted is that the number the card asked for equals the fixture's own sum, and arranging
    /// into that number is only what makes a layout pass happen.</para>
    ///
    /// <para><b>The card is measured directly, not through the window.</b> A host that has already
    /// laid something out answers the <i>window's</i> measure with the previous child's numbers — in
    /// this class that reads <c>0x0</c> for the card that was just attached, even after
    /// <c>InvalidateMeasure</c> and after clearing <c>Content</c> first. Measuring the card itself
    /// needs no window at all for the question "how tall does it ask to be", and returns the right
    /// answer on every pass. The window is kept only so templated children exist.</para>
    ///
    /// <para><b><c>Show()</c> has to happen before the card has a visual root.</b> Without a root a
    /// templated control never instantiates and measures to zero — the failure the hosting
    /// arrangement elsewhere in this file exists to avoid.</para>
    ///
    /// <para><b>One host for the whole class, like <see cref="Host"/>.</b> A window per test leaves
    /// native resources and stacks above the previous one, which shows up later as an unrelated
    /// test failing.</para>
    /// </remarks>
    private static SurfaceCard Unconstrained(SurfaceCard card)
    {
        var host = TallHost;

        host.Content = card;

        // The card's own pass. A second is run because a freshly attached control can report a
        // stale DesiredSize once before its first real measure resolves; the second pass is the
        // number a caller would read afterwards, and it is stable.
        for (var pass = 0; pass < 2; pass++)
        {
            card.Measure(new Size(SurfaceMetrics.CardWidth, double.PositiveInfinity));
            card.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, card.DesiredSize.Height));
        }

        host.UpdateLayout();

        return card;
    }

    /// <summary>The single unconstrained window, shown once.</summary>
    private static Window TallHost
    {
        get
        {
            if (_tallHost is not null)
                return _tallHost;

            _tallHost = new Window { Width = SurfaceMetrics.CardWidth, Height = 2000 };
            _tallHost.Show();

            return _tallHost;
        }
    }

    private static Window? _tallHost;

    /// <summary>
    /// The one <c>List</c> on the card, found by type rather than by key.
    /// </summary>
    /// <remarks>
    /// Every fixture that calls this has exactly one list, so the type identifies it. Reading a
    /// <c>ScrollViewer</c> needs no name, which keeps these assertions about what the list does
    /// rather than about what it is called.
    /// </remarks>
    private static ScrollViewer Scroller(SurfaceCard card) =>
        card.GetVisualDescendants().OfType<ScrollViewer>().Single();

    /// <summary>
    /// The element's top edge, measured down from the card.
    /// </summary>
    /// <remarks>
    /// <b>Why this reads <c>Bounds.Top</c> and does not add a <c>TranslatePoint</c> to it.</b>
    /// <c>Bounds</c> is already relative to the parent panel, so adding the translated offset counts
    /// the displacement twice. The mistake is invisible in the first assertion of a two-element card
    /// — both children would be off by a factor of two, and a difference between them would come out
    /// at twice the right answer, which looks like a plausible slot height. It only surfaces when
    /// somebody checks the numbers against the height they set.
    /// </remarks>
    private static double TopAt(SurfaceCard card, int index) =>
        CardProbe.TopOf(card, CellAt(card, index));

    /// <summary>
    /// A row number nobody used still occupies a row on the card.
    /// </summary>
    /// <remarks>
    /// <para><b>The one criterion the sparse-row reversal turns on.</b> Write rows 1 and 3 and the
    /// second element belongs at 128px, not at 64: row 2 is a row, empty and one slot tall. Before
    /// 2026-11-16 the card squeezed the numbers together and put it at 64, and the design said that
    /// was correct — an order, not a distance.</para>
    ///
    /// <para><b>Why the measurement, and not the arithmetic.</b> <c>SurfaceLayout.PixelTopOf</c>
    /// answers 128 as soon as <c>SlotHeightOf</c> says an empty row is one slot, and
    /// <c>SurfaceLayoutTests</c> already asserts that answer with no screen involved. What that file
    /// cannot check is whether the card <i>draws</i> it: the renderer maps rows to
    /// <c>RowDefinition</c>s on its own, so a card that numbers them consecutively would put the
    /// element at 64 while the pure function said 128, and both test files would be green about
    /// different cards. This asserts the drawn position against a literal, which is the only way the
    /// two ends are tied together.</para>
    ///
    /// <para><b>128 is written as a literal on purpose.</b> Expressing it as
    /// <c>SurfaceLayout.PixelTopOf(...)</c> would compare the renderer against the function it is
    /// being checked against, which is the one comparison this file's header explains is worthless.</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_row_number_nobody_used_still_takes_a_row_on_the_card()
    {
        var card = Card(
            new SurfaceTextElement { Row = 1, Key = "title" },
            new SurfaceTextElement { Row = 3, Key = "note" });

        // The cell's own top edge, read from the card rather than through CardProbe.TopOf: that
        // helper computes a row's top by adding up the rows before it, which answers the same
        // question here but would let a renderer that put this row in the wrong place still report
        // the place the arithmetic predicts. Translating the control's origin into the card's
        // coordinate space asks the layout system what it actually did.
        var top = CellAt(card, 1).TranslatePoint(new Point(0, 0), card)!.Value.Y;

        // Row 3's cell begins two slots down: 64 for the occupied row 1, 64 for the empty row 2, and
        // then FieldInset inside its own row — because a control is pinned 16px below its row's top.
        Assert.Equal(2 * SurfaceMetrics.SlotHeight + SurfaceMetrics.FieldInset, top);

        // Four definitions: rows 1, 2, 3, and the Star remainder that lets a short card fill one
        // screen. Row 2 is a row even though nothing stands in it.
        Assert.Equal(4, card.RowDefinitions.Count);
        Assert.Equal(2, CardProbe.Rows(card).Count);
    }

    // ── Criterion: a line's role changes where nothing is, only what it looks like ──────

    /// <summary>
    /// Promoting a line to a title leaves every pixel of its geometry where it was.
    /// </summary>
    /// <remarks>
    /// <para><b>The same criterion <c>emphasis</c> follows for buttons</b>, and for the same reason:
    /// if appearance could move an element, <c>row</c> would stop being the only thing that decides
    /// position. A card whose rows moved when a line was promoted would also make the editor's
    /// dragged weights mean something different from what the file says.</para>
    ///
    /// <para><b>All three roles in one card, not three cards.</b> Three separate cards would compare
    /// three layout passes and could differ for reasons belonging to none of the roles. One document
    /// with a title, a body and a caption on consecutive rows keeps the passes identical and asks
    /// only where each row landed.</para>
    ///
    /// <para><b>This file does not load the shipped theme</b>, so the sizes here are the default
    /// <c>TextBlock</c> size and the role is invisible. That is deliberate: geometry must be the same
    /// with or without the theme, and a test that needed the theme to hold would be asserting the
    /// theme's business. The sizes themselves are checked in <c>ThemedCardTests</c>, which renders
    /// with it.</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_role_never_moves_the_line()
    {
        // The claim in SurfaceTextElement.Role's remarks is "this field never affects layout", and
        // until now it was only a sentence. The assertion that makes it true is a comparison of the
        // same document drawn under two different roles: same rows, same weights, same key — only
        // the word changes. Every Bounds has to come back identical.
        static (double X, double Y, double Width, double Height)[] BoxesOf(string role)
        {
            var card = Unconstrained(Set([
                new SurfaceTextElement { Row = 1, Key = "a", Role = role },
                new SurfaceTextElement { Row = 2, Key = "b", Role = "body" },
                new SurfaceTextElement { Row = 3, Key = "c", Role = "caption" },
            ]));

            return [.. Enumerable.Range(0, 3).Select(index =>
            {
                var cell = CellAt(card, index);

                // The cell's parent is its row grid, so Bounds alone gives "distance from the top of
                // its row" — the same number for every row. Translating the origin into the card's
                // space is what asks where the row was actually put.
                var origin = cell.TranslatePoint(new Point(0, 0), card)!.Value;
                return (origin.X, origin.Y, cell.Bounds.Width, cell.Bounds.Height);
            })];
        }

        var asBody = BoxesOf("body");
        var asTitle = BoxesOf("title");

        // Not a spot check on one number: all four sides of all three cells. A role that widened a
        // line by two pixels, or nudged it down by one, would leave row tops intact and still be a
        // role that moves things.
        Assert.Equal(asBody, asTitle);

        // And the absolute positions, so "identical" cannot be satisfied by both renders being
        // wrong in the same way: each cell sits FieldInset below its own row's top.
        Assert.Equal(SurfaceMetrics.FieldInset, asTitle[0].Y, tolerance: 0.5);
        Assert.Equal(SurfaceMetrics.SlotHeight + SurfaceMetrics.FieldInset, asTitle[1].Y, tolerance: 0.5);
        Assert.Equal(2 * SurfaceMetrics.SlotHeight + SurfaceMetrics.FieldInset, asTitle[2].Y, tolerance: 0.5);
    }

    /// <summary>
    /// The top edge of the row the element sits in, measured down from the card.
    /// </summary>
    /// <remarks>
    /// <b>Why this is not <see cref="TopAt"/> minus a constant.</b> Since Q100 a control is
    /// <c>FieldHeight</c> tall and sits <c>FieldInset</c> below its row's top, so the control's own top
    /// is not the row's top. The verdict says the element's <c>Top</c> must be
    /// <c>(SlotHeight - FieldHeight) / 2</c> below where its row starts — and testing that needs the
    /// row's start computed from the row structure, not from the control's position. Deriving it as
    /// <c>TopAt(...) - FieldInset</c> would make the assertion below an identity: it would hold for any
    /// inset, including <c>SlotHeight / 2</c>, and would therefore say nothing about which of the two
    /// readings the renderer implements.
    /// </remarks>
    private static double RowTopAt(SurfaceCard card, int index) =>
        CardProbe.RowTopAt(card, index);
}
