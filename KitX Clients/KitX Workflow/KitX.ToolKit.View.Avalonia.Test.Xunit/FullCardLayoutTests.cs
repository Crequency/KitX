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

    private static List<global::Avalonia.Controls.Control> Named(SurfaceCard card) =>
    [
        .. card.GetVisualDescendants().OfType<global::Avalonia.Controls.Control>()
            .Where(c => !string.IsNullOrEmpty(c.Name))
            .OrderBy(c => c.Name, StringComparer.Ordinal),
    ];

    [AvaloniaFact]
    public void Every_element_on_the_document_becomes_a_control_on_the_card()
    {
        // By count and by type, not by name. A test that has to know what each element is called is
        // testing the wiring of names; it also checks less, because four named cells can be present
        // while a fifth element never made it onto the card.
        var card = Card(ScanBook);

        Assert.Equal(7, card.Children.Count);
        Assert.Equal(3, card.Children.OfType<global::Avalonia.Controls.Button>().Count());
        Assert.Single(card.Children.OfType<global::Avalonia.Controls.ScrollViewer>());
        Assert.Single(card.Children.OfType<global::Avalonia.Controls.TextBox>());
        Assert.Equal(2, card.Children.OfType<global::Avalonia.Controls.TextBlock>().Count());
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
        var narrowCells = Geometry(narrow).Where(c => c.Kind != "ScrollViewer").ToList();

        var wide = Card(ScanBook.Replace(
            "\"key\": \"sessions\", \"row\": 4, \"column\": 1, \"columnSpan\": 4",
            "\"key\": \"sessions\", \"row\": 4, \"column\": 1, \"columnSpan\": 1",
            StringComparison.Ordinal));

        var list = wide.Children.OfType<global::Avalonia.Controls.ScrollViewer>().Single();
        Assert.Equal(ColumnWidth, list.Bounds.Width, tolerance: 0.5);
        Assert.Equal(narrowCells, Geometry(wide).Where(c => c.Kind != "ScrollViewer").ToList());
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

    [AvaloniaFact]
    public void A_span_running_past_the_last_column_is_clamped_to_the_edge_rather_than_clipped()
    {
        // Equality, not inequality. `Bounds.Right <= CardWidth` would also pass an implementation
        // that clamped the span down to nothing; only the equation says "clamped to the edge".
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
    private static List<(string Kind, double Top, double Left)> Geometry(SurfaceCard card) =>
    [
        .. card.Children.OfType<global::Avalonia.Controls.Control>()
            .OrderBy(c => c.Bounds.Top)
            .ThenBy(c => c.Bounds.Left)
            .Select(c => (c.GetType().Name, c.Bounds.Top, c.Bounds.Left)),
    ];
}