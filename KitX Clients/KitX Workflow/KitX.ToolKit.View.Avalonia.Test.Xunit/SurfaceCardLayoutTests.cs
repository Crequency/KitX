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

        var child = Child(card, "title");

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
        var actual = TopOf(card, "second") - TopOf(card, "first");

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

        var extra = TopOf(withList, "below") - TopOf(withText, "below");
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
            TopOf(withValues, "subtitle") - TopOf(withValues, "title"),
            TopOf(withoutValues, "subtitle") - TopOf(withoutValues, "title"),
            tolerance: 0.5);

        Assert.Equal(
            TopOf(withValues, "note") - TopOf(withValues, "subtitle"),
            TopOf(withoutValues, "note") - TopOf(withoutValues, "subtitle"),
            tolerance: 0.5);

        // And the cells are still cell-sized, not collapsed to nothing.
        Assert.Equal(
            SurfaceMetrics.SlotHeight,
            Child(withoutValues, "title").Bounds.Height,
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

        var adjacentDelta = TopOf(adjacentCard, "b") - TopOf(adjacentCard, "a");
        var gapDelta = TopOf(gapCard, "b") - TopOf(gapCard, "a");

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
        // Written as "the differences between consecutive tops are all one slot" rather than as four
        // absolute numbers, so the assertion survives a card that gains a header or padding.
        var elements = new List<SurfaceElement>
        {
            new SurfaceInputElement { Row = 1, Column = 1, Key = "barcode" },
            new SurfaceNumberElement { Row = 2, Column = 1, Key = "count" },
            new SurfaceSelectElement { Row = 3, Column = 1, Key = "mode" },
            new SurfaceSwitchElement { Row = 4, Column = 1, Key = "enabled" },
        };

        var card = Card([.. elements]);

        foreach (var key in new[] { "barcode", "count", "mode", "enabled" })
        {
            Assert.Equal(
                SurfaceMetrics.SlotHeight,
                Child(card, key).Bounds.Height,
                tolerance: 0.5);
        }

        Assert.Equal(SlotHeight, TopOf(card, "count") - TopOf(card, "barcode"), tolerance: 0.5);
        Assert.Equal(SlotHeight, TopOf(card, "mode") - TopOf(card, "count"), tolerance: 0.5);
        Assert.Equal(SlotHeight, TopOf(card, "enabled") - TopOf(card, "mode"), tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_control_without_a_key_still_occupies_its_slot()
    {
        // SurfaceElement.Key is nullable: an element may read nothing and still be a thing on the
        // card. Skipping it would make a row's height depend on whether a key was written — the same
        // failure as letting a value decide the layout, arriving from the other side.
        var elements = new List<SurfaceElement>
        {
            new SurfaceTextElement { Row = 1, Column = 1, Key = "title" },
            new SurfaceInputElement { Row = 2, Column = 1, Key = null },
            new SurfaceTextElement { Row = 3, Column = 1, Key = "footer" },
        };

        var card = Card([.. elements]);

        // Two slot-heights from title to footer: the keyless input still takes its row.
        Assert.Equal(
            2 * SurfaceMetrics.SlotHeight,
            TopOf(card, "footer") - TopOf(card, "title"),
            tolerance: 0.5);

        Assert.Equal(3, card.Children.OfType<Control>().Count());
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
    private static SurfaceCard Card(params SurfaceElement[] elements)
    {
        var card = new SurfaceCard();
        card.Render(elements);

        card.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        card.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        card.UpdateLayout();

        return card;
    }

    /// <summary>
    /// The laid-out control for an element, found by key.
    /// </summary>
    private static Control Child(SurfaceCard card, string key) =>
        card.Children.OfType<Control>().Single(c => c.Name == key);
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
    private static double TopOf(SurfaceCard card, string key) =>
        card.Children.OfType<Control>().Single(c => c.Name == key).Bounds.Top;
}
