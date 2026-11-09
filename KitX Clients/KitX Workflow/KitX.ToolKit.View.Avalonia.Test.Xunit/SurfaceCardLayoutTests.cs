using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
        var card = Card(new SurfaceTextElement { Row = 1, Column = 1, Key = "title" });

        var child = CellAt(card, 0);

        Assert.True(card.Bounds.Width > 0, $"card width is {card.Bounds.Width}");
        Assert.True(card.Bounds.Height > 0, $"card height is {card.Bounds.Height}");
        Assert.True(child.Bounds.Height > 0, $"child height is {child.Bounds.Height}");
    }

    // ── The criterion ────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Two_elements_in_consecutive_rows_are_one_slot_height_apart()
    {
        // The simplest case the criterion covers, and the one a renderer gets wrong first: rows are
        // stacked tightly, not spread to fill the card.
        var elements = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Column = 1, Key = "first" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "second" },
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
            new SurfaceListElement { Row = 1, Column = 1, Key = "sessions" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "below" });

        var withText = Card(
            new SurfaceTextElement { Row = 1, Column = 1, Key = "sessions" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "below" });

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
        // The card draws a Text the same way whether or not a value arrived, so the two layouts agree
        // by construction. Saying so out loud matters: a future renderer that starts hiding empty
        // cells fails here instead of silently reflowing the card.
        var elements = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Column = 1, Key = "title" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "subtitle" },
            new SurfaceTextElement { Row = 3, Column = 1, Key = "note" },
        };

        var withValues = Card([.. elements]);
        var withoutValues = Card([.. elements]);

        Assert.Equal(
            TopAt(withValues, 1) - TopAt(withValues, 0),
            TopAt(withoutValues, 1) - TopAt(withoutValues, 0),
            tolerance: 0.5);

        Assert.Equal(
            TopAt(withValues, 2) - TopAt(withValues, 1),
            TopAt(withoutValues, 2) - TopAt(withoutValues, 1),
            tolerance: 0.5);

        // And the cells are still cell-sized, not collapsed to nothing.
        Assert.Equal(
            SurfaceMetrics.SlotHeight,
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
            new SurfaceTextElement { Row = 1, Column = 1, Key = "a" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "b" },
        };

        var withGap = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Column = 1, Key = "a" },
            new SurfaceTextElement { Row = 5, Column = 1, Key = "b" },
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
            new SurfaceInputElement { Row = 1, Column = 1 },
            new SurfaceNumberElement { Row = 2, Column = 1 },
            new SurfaceSelectElement { Row = 3, Column = 1 },
            new SurfaceSwitchElement { Row = 4, Column = 1 },
        };

        var card = Card([.. elements]);

        var cells = card.Children
            .OfType<Control>()
            .OrderBy(c => c.Bounds.Top)
            .ToList();

        Assert.Equal(4, cells.Count);

        foreach (var cell in cells)
            Assert.Equal(SurfaceMetrics.SlotHeight, cell.Bounds.Height, tolerance: 0.5);

        var tops = cells.Select(c => c.Bounds.Top).ToList();
        foreach (var (a, b) in tops.Zip(tops.Skip(1)))
            Assert.Equal(SurfaceMetrics.SlotHeight, b - a, tolerance: 0.5);
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
            new SurfaceTextElement { Row = 1, Column = 1, Key = "title" },
            new SurfaceButtonElement { Row = 2, Column = 1, Label = "Save" },
            new SurfaceTextElement { Row = 3, Column = 1, Key = "footer" },
        };

        var card = Card([.. elements]);

        // Two slot-heights from title to footer: the keyless input still takes its row.
        Assert.Equal(
            2 * SurfaceMetrics.SlotHeight,
            TopAt(card, 2) - TopAt(card, 0),
            tolerance: 0.5);

        Assert.Equal(3, card.Children.OfType<Control>().Count());
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
        var listSlot = new SurfaceListElement { Row = 1, Column = 1, Key = "sessions" };
        var below = new SurfaceTextElement { Row = 2, Column = 1, Key = "footer" };

        var card = Card([listSlot, below]);

        // The row grew by K slots — that half already holds.
        Assert.Equal(
            ListSlots * SurfaceMetrics.SlotHeight,
            TopAt(card, 1),
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
            [new SurfaceListElement { Row = 1, Column = 1, Key = "sessions" },
             new SurfaceTextElement { Row = 2, Column = 1, Key = "footer" }],
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
    private static Control CellAt(SurfaceCard card, int index) =>
        card.Children.OfType<Control>().OrderBy(c => c.Bounds.Top).ElementAt(index);

    /// <summary>
    /// The one <c>List</c> on the card, found by type rather than by key.
    /// </summary>
    /// <remarks>
    /// Every fixture that calls this has exactly one list, so the type identifies it. Reading a
    /// <c>ScrollViewer</c> needs no name, which keeps these assertions about what the list does
    /// rather than about what it is called.
    /// </remarks>
    private static ScrollViewer Scroller(SurfaceCard card) =>
        card.Children.OfType<ScrollViewer>().Single();

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
    private static double TopAt(SurfaceCard card, int index) => CellAt(card, index).Bounds.Top;
}
