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
    private const double ColumnWidth = SurfaceMetrics.CardWidth / SurfaceMetrics.CardColumns;

    private const string ScanBook = """
        {
          "schemaVersion": 1,
          "columns": 4,
          "elements": [
            { "type": "Input",  "name": "barcode", "row": 1, "column": 1, "columnSpan": 4 },
            { "type": "Text",   "key": "title", "name": "heading", "row": 2, "column": 1, "columnSpan": 3 },
            { "type": "Text",   "key": "cover", "name": "thumb", "row": 2, "column": 4, "columnSpan": 1 },
            { "type": "Button", "label": "Scan",     "events": ["scan"],     "row": 3, "column": 1, "columnSpan": 1 },
            { "type": "Button", "label": "Add",      "events": ["add"],      "row": 3, "column": 2, "columnSpan": 1 },
            { "type": "Button", "label": "Sessions", "events": ["sessions"], "row": 3, "column": 3, "columnSpan": 2 },
            { "type": "List",   "key": "sessions", "row": 4, "column": 1, "columnSpan": 4, "itemTitle": "startedAt" }
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
        Assert.Equal(7, card.Children.Count);
        Assert.Equal(3, card.Children.OfType<global::Avalonia.Controls.Button>().Count());
        Assert.Single(card.Children.OfType<global::Avalonia.Controls.TextBox>());
        Assert.Equal(2, card.Children.OfType<global::Avalonia.Controls.TextBlock>().Count());

        // The List cell is the group container, and the one scroller inside it is the list itself.
        // Not "one ScrollViewer in the visual tree": ComboBox and NumericUpDown are built from
        // templates that contain a ScrollViewer of their own, and this card has both. Counting by
        // type over the whole tree would find three and say the List was drawn twice.
        var listCell = Assert.Single(card.Children.OfType<global::Avalonia.Controls.Border>());
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
            { "schemaVersion": 1, "columns": 4, "elements": [
              { "type": "Input", "row": 1, "column": 1, "name": "barcode" },
              { "type": "Text",  "key": "title", "row": 2, "column": 1 }
            ] }
            """);

        var cells = card.Children.OfType<global::Avalonia.Controls.Control>().ToList();

        Assert.Equal("barcode", Assert.Single(cells, c => c is global::Avalonia.Controls.TextBox).Name);
        Assert.Null(Assert.Single(cells, c => c is global::Avalonia.Controls.TextBlock).Name);
        Assert.DoesNotContain(cells, c => c.Name == string.Empty);
    }

    [AvaloniaFact]
    public void Three_buttons_on_one_row_sit_side_by_side_in_column_order()
    {
        var card = Card(ScanBook);
        var buttons = card.GetVisualDescendants().OfType<global::Avalonia.Controls.Button>()
            .OrderBy(b => b.Bounds.Left).ToList();
        Assert.Equal(3, buttons.Count);
        Assert.All(buttons, b => Assert.Equal(buttons[0].Bounds.Top, b.Bounds.Top, tolerance: 0.5));
        Assert.Equal(ColumnWidth, buttons[1].Bounds.Left - buttons[0].Bounds.Left, tolerance: 0.5);
        Assert.Equal(ColumnWidth, buttons[2].Bounds.Left - buttons[1].Bounds.Left, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void Moving_one_button_to_its_own_row_lowers_it_and_leaves_the_others()
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
        Assert.Equal(beforeButtons[1].Bounds.Top + Slot, afterButtons[1].Bounds.Top, tolerance: 0.5);
        Assert.Equal(beforeButtons[0].Bounds.Top, afterButtons[0].Bounds.Top, tolerance: 0.5);
        Assert.Equal(beforeButtons[2].Bounds.Top, afterButtons[2].Bounds.Top, tolerance: 0.5);
        Assert.Equal(beforeButtons[0].Bounds.Left, afterButtons[0].Bounds.Left, tolerance: 0.5);
        Assert.Equal(beforeButtons[2].Bounds.Left, afterButtons[2].Bounds.Left, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void Widening_an_element_changes_its_width_and_moves_nothing_else()
    {
        var narrow = Card(ScanBook);
        var narrowCells = Geometry(narrow).Where(c => c.Kind != ListGroupKind).ToList();

        var wide = Card(ScanBook.Replace(
            "\"key\": \"sessions\", \"row\": 4, \"column\": 1, \"columnSpan\": 4",
            "\"key\": \"sessions\", \"row\": 4, \"column\": 1, \"columnSpan\": 1",
            StringComparison.Ordinal));

        var list = wide.Children.OfType<global::Avalonia.Controls.Border>().Single();
        Assert.Equal(ColumnWidth, list.Bounds.Width, tolerance: 0.5);
        Assert.Equal(narrowCells, Geometry(wide).Where(c => c.Kind != ListGroupKind).ToList());
    }

    [AvaloniaFact]
    public void A_span_wider_than_one_column_makes_the_element_actually_wider()
    {
        // The three buttons on row 3 are columnSpan 1, 1 and 2 — so the third one is twice as wide.
        // Before this was implemented every element occupied exactly one column, and nothing failed:
        // the fixture declared spans 4, 3, 2 and 4, and no assertion read a width.
        var card = Card(ScanBook);
        var buttons = card.GetVisualDescendants().OfType<global::Avalonia.Controls.Button>()
            .OrderBy(b => b.Bounds.Left).ToList();

        Assert.Equal(3, buttons.Count);
        Assert.Equal(ColumnWidth, buttons[0].Bounds.Width, tolerance: 0.5);
        Assert.Equal(ColumnWidth, buttons[1].Bounds.Width, tolerance: 0.5);
        Assert.Equal(2 * ColumnWidth, buttons[2].Bounds.Width, tolerance: 0.5);
    }

    [AvaloniaFact]
    public void A_span_that_reaches_the_last_column_ends_at_the_card_edge()
    {
        var card = Card(ScanBook);
        var last = card.GetVisualDescendants().OfType<global::Avalonia.Controls.Button>()
            .OrderBy(b => b.Bounds.Left).Last();

        Assert.Equal(SurfaceMetrics.CardWidth, last.Bounds.Right, tolerance: 0.5);
    }

    /// <summary>
    /// A span wider than the columns that remain stops at the card edge instead of running past
    /// it — and it stops there even though nothing in <c>SurfaceCard</c> clamps it.
    /// </summary>
    /// <remarks>
    /// The property is ours; the mechanism today is Avalonia's. <c>Grid</c> clamps an over-wide
    /// <c>ColumnSpan</c> at layout time: with four columns, a column of 2 and a span of 4 and a span
    /// of 3 both produce width 292 and right edge 390. <c>SurfaceCard</c> used to repeat the clamp
    /// with <c>Math.Min</c>, and deleting it left every test in this file green — proof that it
    /// could not change any output. It also threw on the input it was meant to defend against: past
    /// the last column the arithmetic goes to zero or negative, and <c>Grid.SetColumnSpan</c>
    /// rejects both.
    ///
    /// So this is a **fourth-layer** criterion: it is true today, and what makes it true is not our
    /// code. It is kept because a renderer that swaps the container, or that lets <c>CardColumns</c>
    /// drift from the Grid's column count, would stop being clamped — and this is the assertion that
    /// would say so. Anything touching <c>Grid.SetColumnSpan</c> should expect to land here.
    /// </remarks>
    [AvaloniaFact]
    public void A_span_running_past_the_last_column_stops_at_the_card_edge()
    {
        // Equality, not inequality. `Bounds.Right <= CardWidth` would also pass an implementation
        // that clamped the span down to nothing; only the equation says "stops at the edge".
        //
        // The element starts at column 2 and asks for 4, so 3 columns remain. That choice matters:
        // at column 4 the clamp and "one column" both end at the right edge, and this test would
        // pass with no column span at all.
        var card = Card("""
            { "schemaVersion": 1, "columns": 4, "elements": [
              { "type": "Text", "key": "tail", "row": 1, "column": 2, "columnSpan": 4 }
            ] }
            """);

        var cell = card.Children.OfType<global::Avalonia.Controls.Control>().Single();
        Assert.Equal(3 * ColumnWidth, cell.Bounds.Width, tolerance: 0.5);
        Assert.Equal(SurfaceMetrics.CardWidth, cell.Bounds.Right, tolerance: 0.5);
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
        var softBoth = Card(TwoButtons("quiet", "quiet"));
        var mixed = Card(TwoButtons("primary", "quiet"));

        Assert.Equal(Geometry(plain), Geometry(loudBoth));
        Assert.Equal(Geometry(plain), Geometry(softBoth));
        Assert.Equal(Geometry(plain), Geometry(mixed));

        // The classes are the other half: an implementation that ignored the word entirely would pass
        // every comparison above, because ignoring it changes nothing either. This card puts three
        // values side by side — one primary, one quiet, and — on a second reading — no emphasis at
        // all, so the fallback to `normal` is covered too.
        Assert.Equal(["kitx-primary", "kitx-quiet"], ButtonClasses(mixed));
        Assert.Equal(["kitx-button", "kitx-button"], ButtonClasses(plain));
    }

    /// <summary>
    /// Two buttons on one row plus a list below, with the two emphases given.
    /// </summary>
    private static string TwoButtons(string first, string second) => $$"""
        { "schemaVersion": 1, "columns": 4, "elements": [
          { "type": "Button", "label": "Left",  "events": ["left"],  "row": 1, "column": 1, "emphasis": "{{first}}" },
          { "type": "Button", "label": "Right", "events": ["right"], "row": 1, "column": 2, "emphasis": "{{second}}" },
          { "type": "List",   "key": "sessions", "row": 2, "column": 1, "columnSpan": 4 }
        ] }
        """;

    /// <summary>
    /// The class each button carries, in the order the document declares them.
    /// </summary>
    /// <remarks>
    /// Cards do not preserve a lookup order a test can rely on, so the buttons are read back in a
    /// fixed order — left to right within a row, top to bottom across rows — and the fixture above is
    /// written so that this is the declared order too.
    /// </remarks>
    private static List<string> ButtonClasses(SurfaceCard card) =>
    [
        .. card.Children.OfType<global::Avalonia.Controls.Button>()
            .OrderBy(b => b.Bounds.Top)
            .ThenBy(b => b.Bounds.Left)
            .Select(b => string.Join(" ", b.Classes.Where(c => c.StartsWith("kitx-", StringComparison.Ordinal)))),
    ];

    /// <summary>
    /// Where every direct child of the card sits, in reading order.
    /// </summary>
    /// <remarks>
    /// The list's cell is a group container, so a card with one list yields a <c>Border</c> here.
    /// Callers comparing two cards filter that kind out: the container is the one cell whose type is
    /// not the element's own, and its position is already implied by the cells around it.
    /// </remarks>
    private static List<(string Kind, double Top, double Left)> Geometry(SurfaceCard card) =>
    [
        .. card.Children.OfType<global::Avalonia.Controls.Control>()
            .OrderBy(c => c.Bounds.Top)
            .ThenBy(c => c.Bounds.Left)
            .Select(c => (c.GetType().Name, c.Bounds.Top, c.Bounds.Left)),
    ];
}