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
    public void A_list_pushes_everything_below_it_down_by_its_full_height()
    {
        // The synthetic input again, this time in pixels rather than slots: all seven real element
        // kinds are one slot tall, so a renderer that ignored the List's extra slots would pass every
        // other assertion here.
        //
        // Asserted from the element BELOW the list, not from the list itself: at this stage the card
        // only builds a view for Text, and the point being tested is where the next row lands, not
        // how a list looks. Reading the position of something the card did draw keeps the assertion
        // about the one thing that is finished.
        var elements = new List<SurfaceElement>
        {
            new SurfaceListElement { Row = 1, Column = 1, Key = "sessions" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "below" },
        };

        var card = Card([.. elements]);
        var actual = TopOf(card, "below");

        var expected = SurfaceLayout.PixelTopOf(elements, elements[1], SlotHeight, ListSlots)
                     - SurfaceLayout.PixelTopOf(elements, elements[0], SlotHeight, ListSlots);

        Assert.Equal(expected, actual, tolerance: 0.5);
        Assert.Equal(SurfaceMetrics.ListSlotCount * SlotHeight, expected);
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
