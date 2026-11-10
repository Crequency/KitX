using global::Avalonia.Controls;
using global::Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View.Avalonia;
using System.Text.Json;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

public sealed class FullCardLayoutTests
{
    private const double Slot = SurfaceMetrics.SlotHeight;

    /// <summary>The gap the renderer leaves between two elements on one row.</summary>
    private const double Gap = SurfaceMetrics.FieldGap;

    /// <summary>
    /// One part's share of a row: the card's width minus that row's gaps, divided by its parts.
    /// </summary>
    /// <remarks>
    /// <b>Why the gaps come out first, and why this takes two numbers.</b> Q101's formula is
    /// <c>available × weight_i / Σ weights</c>, and <c>available</c> is the card's width minus the
    /// <c>(n − 1)</c> gaps. Dividing the full width instead and then leaving room for the gaps would
    /// make the row overrun the card: three parts of a 390-wide card plus two 8-pixel gaps add up to
    /// 406.
    ///
    /// The two numbers are separate because they answer different questions: <paramref name="elementsInRow"/>
    /// is how many controls share the row (that is what sets the gap count), and
    /// <paramref name="partsInRow"/> is the sum of their weights (that is what sets each share). They
    /// happen to be equal only on a row whose weights are all 1.
    /// </remarks>
    private static double Share(int elementsInRow, int partsInRow) =>
        (SurfaceMetrics.CardWidth - ((elementsInRow - 1) * Gap)) / partsInRow;

    private const string ScanBook = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Input",  "name": "barcode", "row": 1, "weight": 4 },
            { "type": "Text",   "key": "title", "name": "heading", "row": 2, "weight": 3 },
            { "type": "Text",   "key": "cover", "name": "thumb", "row": 2, "weight": 1 },
            { "type": "Button", "label": "Scan",     "events": ["scan"],     "row": 3, "weight": 1 },
            { "type": "Button", "label": "Add",      "events": ["add"],      "row": 3, "weight": 1 },
            { "type": "Button", "label": "Sessions", "events": ["sessions"], "row": 3, "weight": 2 },
            { "type": "List",   "key": "sessions", "row": 4, "weight": 4, "itemTitle": "startedAt" }
          ]
        }
        """;

    /// <summary>
    /// The runtime type name of a List's group container, as <see cref="Geometry"/> reports it.
    /// </summary>
    /// <remarks>
    /// Since Q99 a List is drawn as a <c>Border</c> holding the scroller, so that the visible area is
    /// one slot tall while the slot itself is six. Any geometry comparison that means "everything
    /// except the list" has to skip that container, and it has to skip it by a name this file owns
    /// rather than by a literal scattered through the assertions.
    /// </remarks>
    private const string ListGroupKind = "Border";

    private static SurfaceCard Card(string json)
    {
        var document = JsonSerializer.Deserialize<SurfaceDocument>(json)!;
        var card = new SurfaceCard();
        card.SetDocument(document);

        var window = Host;
        window.Content = card;
        window.Measure(new global::Avalonia.Size(card.Width, card.Height));
        window.Arrange(new global::Avalonia.Rect(0, 0, card.Width, card.Height));
        window.UpdateLayout();
        return card;
    }

    private static global::Avalonia.Controls.Window Host
    {
        get
        {
            if (_host is not null) return _host;
            _host = new global::Avalonia.Controls.Window { Width = SurfaceMetrics.CardWidth, Height = SurfaceMetrics.CardHeight };
            _host.Show();
            return _host;
        }
    }

    private static global::Avalonia.Controls.Window? _host;

    // There used to be a `Named(card)` helper here that returned the keyed cells ordered by
    // Control.Name. It is gone, and the reason is worth stating because it will be reached for
    // again: since Q71 an element's Name is its upload identity and exists only where the author
    // wrote one, so most cells have no name and the helper silently returned a subset. A layout
    // assertion built on it therefore depended on which elements happened to be named. Keeping it
    // around as an unused-but-available shortcut would have made "find cells by name" look like a
    // normal technique that merely fell out of favour, when what actually happened is that element
    // identity was removed from the model. Use the type-and-order helpers below instead.

    [AvaloniaFact]
    public void Every_element_on_the_document_becomes_a_control_on_the_card()
    {
        // By count and by type, not by name. A test that has to know what each element is called is
        // testing the wiring of names; it also checks less, because four named cells can be present
        // while a fifth element never made it onto the card.
        var card = Card(ScanBook);

        // Seven cells, one per element. The List's cell is a Border that holds the scroller — the
        // group container introduced with Q99 — so the cell count still counts it once.
        //
        // Through CardProbe because since Q101 the card's own children are rows, not elements: the
        // four elements above sit in four rows, so card.Children.Count would read 4 here and the
        // assertion would still look like it was counting elements.
        var elements = CardProbe.Elements(card);

        Assert.Equal(7, elements.Count);
        Assert.Equal(3, elements.OfType<global::Avalonia.Controls.Button>().Count());
        Assert.Single(elements.OfType<global::Avalonia.Controls.TextBox>());
        Assert.Equal(2, elements.OfType<global::Avalonia.Controls.TextBlock>().Count());

        // The List cell is the group container, and the one scroller inside it is the list itself.
        // Not "one ScrollViewer in the visual tree": ComboBox and NumericUpDown are built from
        // templates that contain a ScrollViewer of their own, and this card has both. Counting by
        // type over the whole tree would find three and say the List was drawn twice.
        var listCell = Assert.Single(elements.OfType<global::Avalonia.Controls.Border>());
        Assert.Single(listCell.GetVisualDescendants().OfType<global::Avalonia.Controls.ScrollViewer>());
    }

    [AvaloniaFact]
    public void An_element_without_a_name_has_no_address()
    {
        // Control.Name is Avalonia's element address, and since Q71 the only thing that may fill it
        // is the element's own Name. The Input below has one; the Text does not. The assertion is
        // that the unnamed one is left unassigned rather than given "" — an empty string is still an
        // address, one whose content happens to be empty, and a later test could start using it as a
        // lookup key with nothing visible to say the choice was accidental.
        var card = Card("""
            { "schemaVersion": 1, "layoutVersion": 1, "elements": [
              { "type": "Input", "row": 1, "name": "barcode" },
              { "type": "Text",  "key": "title", "row": 2 }
            ] }
            """);

        var cells = CardProbe.Elements(card).ToList();

        Assert.Equal("barcode", Assert.Single(cells, c => c is global::Avalonia.Controls.TextBox).Name);
        Assert.Null(Assert.Single(cells, c => c is global::Avalonia.Controls.TextBlock).Name);
        Assert.DoesNotContain(cells, c => c.Name == string.Empty);
    }

    [AvaloniaFact]
    public void Three_buttons_on_one_row_sit_side_by_side_in_declaration_order()
    {
        var card = Card(ScanBook);

        var buttons = card.GetVisualDescendants().OfType<global::Avalonia.Controls.Button>()
            .OrderBy(b => b.Bounds.Left).ToList();

        Assert.Equal(3, buttons.Count);

        // All three share row 3, so each one's top matches the first one's. Since Q101 there is no
        // column index to compare: the row is one strip, and "side by side" means each starts where
        // the previous one ended, plus the gap. The weights on that row are 1, 1 and 2 out of 4, so
        // the third is twice the width of the others and the three fill the row exactly.
        Assert.All(buttons, b => Assert.Equal(buttons[0].Bounds.Top, b.Bounds.Top, tolerance: 0.5));
        Assert.Equal(Share(3, 4), buttons[0].Bounds.Width, tolerance: 0.5);
        Assert.Equal(Share(3, 4), buttons[1].Bounds.Width, tolerance: 0.5);
        Assert.Equal(2 * Share(3, 4), buttons[2].Bounds.Width, tolerance: 0.5);

        // Each one starts one gap past where the previous one ended.
        Assert.Equal(buttons[0].Bounds.Right + Gap, buttons[1].Bounds.Left, tolerance: 0.5);
        Assert.Equal(buttons[1].Bounds.Right + Gap, buttons[2].Bounds.Left, tolerance: 0.5);
        Assert.Equal(SurfaceMetrics.CardWidth, buttons[2].Bounds.Right, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void Moving_one_button_to_its_own_row_lowers_it_and_leaves_the_other_rows_alone()
    {
        var before = Card(ScanBook);
        var beforeButtons = before.GetVisualDescendants().OfType<global::Avalonia.Controls.Button>()
            .OrderBy(b => b.Bounds.Left).ToList();

        var after = Card(ScanBook.Replace(
            "label\": \"Add\",      \"events\": [\"add\"],      \"row\": 3",
            "label\": \"Add\",      \"events\": [\"add\"],      \"row\": 4",
            StringComparison.Ordinal));
        var afterButtons = after.GetVisualDescendants().OfType<global::Avalonia.Controls.Button>()
            .OrderBy(b => b.Bounds.Left).ToList();

        Assert.Equal(beforeButtons.Count, afterButtons.Count);

        // The moved button sits exactly one slot lower. Rows 3 and 4 are neighbours — both are one
        // slot tall, and only the List's row grows — so moving down by one row is moving down by
        // exactly one slot.
        Assert.Equal(
            CardProbe.TopOf(before, beforeButtons[1]) + Slot,
            CardProbe.TopOf(after, afterButtons[1]),
            tolerance: 0.5);

        // The two that stayed are on the same rows at the same tops.
        Assert.Equal(CardProbe.TopOf(before, beforeButtons[0]), CardProbe.TopOf(after, afterButtons[0]), tolerance: 0.5);
        Assert.Equal(CardProbe.TopOf(before, beforeButtons[2]), CardProbe.TopOf(after, afterButtons[2]), tolerance: 0.5);

        // <b>Their widths necessarily change, and that is Q101 working rather than a regression.</b>
        // Row 3 held weights 1, 1 and 2 (four parts over three elements); with the middle one gone it
        // holds 1 and 2 (three parts over two elements), so each survivor takes a larger share of a
        // row that also has one fewer gap to subtract.
        //
        // Note the direction of the lefts: the last button starts *earlier* after the move, not later
        // — it grew, and one gap disappeared ahead of it. Asserting that its left moved right would
        // have been asserting the shape of the arithmetic rather than the rule. What the rule
        // promises its survivors is width, so width is what is asserted.
        Assert.True(
            afterButtons[0].Bounds.Width > beforeButtons[0].Bounds.Width,
            "the first survivor should take more of a row that has one fewer element");
        Assert.True(
            afterButtons[2].Bounds.Width > beforeButtons[2].Bounds.Width,
            "the second survivor should take more of a row that has one fewer element");

        // Both survivors still fill the row between them: their widths plus one gap are the card.
        Assert.Equal(
            SurfaceMetrics.CardWidth,
            afterButtons[0].Bounds.Width + Gap + afterButtons[2].Bounds.Width,
            tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_row_divides_its_width_by_weight_and_leaves_other_rows_alone()
    {
        var asDeclared = Card(ScanBook);
        var swapped = Card(ScanBook.Replace(
            "\"key\": \"title\", \"name\": \"heading\", \"row\": 2, \"weight\": 3",
            "\"key\": \"title\", \"name\": \"heading\", \"row\": 2, \"weight\": 9",
            StringComparison.Ordinal));

        // Row 2 holds two elements: title (weight 3) and cover (weight 1), four parts in total. Give
        // title nine parts instead and the split becomes 9:1 — both widths must follow, and nothing
        // else on the card may move.
        var wide = Share(2, 10) * 9;
        var thin = Share(2, 10) * 1;

        var titleAfter = CardProbe.Elements(swapped).OfType<global::Avalonia.Controls.TextBlock>()
            .Single(t => t.Name == "heading");
        var coverAfter = CardProbe.Elements(swapped).OfType<global::Avalonia.Controls.TextBlock>()
            .Single(t => t.Name == "thumb");

        Assert.Equal(wide, titleAfter.Bounds.Width, tolerance: 0.5);
        Assert.Equal(thin, coverAfter.Bounds.Width, tolerance: 0.5);

        // Same row, same top: the row is one slot tall however its parts are split.
        Assert.Equal(titleAfter.Bounds.Top, coverAfter.Bounds.Top, tolerance: 0.5);

        // <b>Every other row is exactly where it was.</b> This is the half that catches a renderer
        // that lets one row's weights leak into another — which is what a single shared column set
        // would do, because a shared set has to pick one normalisation for all rows at once.
        var before = CardProbe.Elements(asDeclared)
            .Select(e => (e.GetType().Name, CardProbe.TopOf(asDeclared, e)))
            .OrderBy(t => t.Item2).ToList();
        var after = CardProbe.Elements(swapped)
            .Select(e => (e.GetType().Name, CardProbe.TopOf(swapped, e)))
            .OrderBy(t => t.Item2).ToList();
        Assert.Equal(before, after);
    }

    /// <summary>
    /// An element alone on its row fills the whole row, whatever weight it asks for.
    /// </summary>
    /// <remarks>
    /// <b>Why "whatever weight" is the point.</b> Q102 says a row of one means 100%, and it says so
    /// instead of offering a "natural width, right aligned" opt-out. That is a real decision rather
    /// than a convenience: a weight is a share of a row, and with one element the only share that
    /// exists is all of it. An implementation that treated <c>weight: 1</c> as "one part out of some
    /// implied four" would render a quarter-width element here, and this is the assertion that says
    /// so — the earlier `columnSpan` model could express the same card two ways, and this one cannot.
    ///
    /// The two cards below differ only in the weight. Their element must land in the same place,
    /// because a lone element's weight has nothing to divide.
    /// </remarks>
    [AvaloniaFact]
    public void An_element_alone_on_its_row_fills_the_row_whatever_weight_it_asks_for()
    {
        static string Lone(string weight) => $$"""
            { "schemaVersion": 1, "layoutVersion": 1, "elements": [
              { "type": "Text", "key": "tail", "row": 1, "weight": {{weight}} }
            ] }
            """;

        foreach (var weight in new[] { "1", "3", "9" })
        {
            var cell = Assert.Single(CardProbe.Elements(Card(Lone(weight))));

            Assert.Equal(SurfaceMetrics.CardWidth, cell.Bounds.Width, tolerance: 0.5);
            Assert.Equal(0d, cell.Bounds.Left, tolerance: 0.5);
            Assert.Equal(SurfaceMetrics.CardWidth, cell.Bounds.Right, tolerance: 0.5);
        }
    }

    /// <summary>
    /// Every control the card added, as (type, top, left), in top-to-bottom order.
    /// </summary>
    /// <remarks>
    /// Keyed by type rather than by <c>Control.Name</c>, because since Q71 a name is an element's
    /// upload identity and exists only where the author wrote one. This fixture is arranged so that
    /// every control has a distinct type, which is what makes the sequence comparable — and it means
    /// these assertions say nothing about what anything is called.
    /// </remarks>
    [AvaloniaFact]
    public void Emphasis_changes_how_a_button_looks_and_nothing_about_where_it_is()
    {
        // Q98: emphasis is a class, not a size. Each card below is the same document with one word
        // changed, so their geometry must be identical — including the cells that are not buttons,
        // because a wider primary would push the list down as well.
        //
        // Asserting geometry rather than the class name is the point. A test that read
        // "the button has class kitx-primary" would pass for an implementation that also set FontSize
        // to 18, and that implementation would move the list.
        var plain = Card(TwoButtons("normal", "normal"));
        var loudBoth = Card(TwoButtons("primary", "primary"));
        var dangerousBoth = Card(TwoButtons("danger", "danger"));
        var mixed = Card(TwoButtons("primary", "danger"));

        Assert.Equal(Geometry(plain), Geometry(loudBoth));
        Assert.Equal(Geometry(plain), Geometry(dangerousBoth));
        Assert.Equal(Geometry(plain), Geometry(mixed));

        // The classes are the other half: an implementation that ignored the word entirely would pass
        // every comparison above, because ignoring it changes nothing either. This card puts three
        // values side by side — one primary, one danger, and — on a second reading — no emphasis at
        // all, so the fallback to `normal` is covered too.
        Assert.Equal(["kitx-primary", "kitx-danger"], ButtonClasses(mixed));
        Assert.Equal(["kitx-button", "kitx-button"], ButtonClasses(plain));
    }

    /// <summary>
    /// Two buttons on one row plus a list below, with the two emphases given.
    /// </summary>
    private static string TwoButtons(string first, string second) => $$"""
        { "schemaVersion": 1, "layoutVersion": 1, "elements": [
          { "type": "Button", "label": "Left",  "events": ["left"],  "row": 1, "emphasis": "{{first}}" },
          { "type": "Button", "label": "Right", "events": ["right"], "row": 1, "emphasis": "{{second}}" },
          { "type": "List",   "key": "sessions", "row": 2, "weight": 4 }
        ] }
        """;

    /// <summary>
    /// The class each button carries, in the order the document declares them.
    /// </summary>
    /// <remarks>
    /// Cards do not preserve a lookup order a test can rely on, so the buttons are read back in a
    /// fixed order — left to right within a row, top to bottom across rows — and the fixture above is
    /// written so that this is the declared order too. Since Q101 the top of an element is the top of
    /// its row plus its own offset, so the sort goes through <see cref="CardProbe.TopOf"/>.
    /// </remarks>
    private static List<string> ButtonClasses(SurfaceCard card) =>
    [
        .. CardProbe.Elements(card).OfType<global::Avalonia.Controls.Button>()
            .OrderBy(b => CardProbe.TopOf(card, b))
            .ThenBy(b => b.Bounds.Left)
            .Select(b => string.Join(" ", b.Classes.Where(c => c.StartsWith("kitx-", StringComparison.Ordinal)))),
    ];

    /// <summary>
    /// Where every element on the card sits, in reading order, measured down from the card.
    /// </summary>
    /// <remarks>
    /// The list's cell is a group container, so a card with one list yields a <c>Border</c> here.
    /// Callers comparing two cards filter that kind out: the container is the one cell whose type is
    /// not the element's own, and its position is already implied by the cells around it.
    ///
    /// <b>Why the top comes from <see cref="CardProbe.TopOf"/> rather than from <c>Bounds</c>.</b>
    /// Since Q101 each row is its own <c>Grid</c>, so a control's <c>Bounds.Top</c> is measured from
    /// the top of its row, not from the top of the card. Every row would read zero, and two cards
    /// with different row orders would compare equal.
    /// </remarks>
    private static List<(string Kind, double Top, double Left)> Geometry(SurfaceCard card) =>
    [
        .. CardProbe.Elements(card)
            .Select(c => (c.GetType().Name, CardProbe.TopOf(card, c), c.Bounds.Left))
            .OrderBy(t => t.Item2)
            .ThenBy(t => t.Item3),
    ];
}