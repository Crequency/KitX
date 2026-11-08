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
            { "type": "Input",  "key": "barcode", "row": 1, "column": 1, "columnSpan": 4 },
            { "type": "Text",   "key": "title",   "row": 2, "column": 1, "columnSpan": 3 },
            { "type": "Text",   "key": "cover",   "row": 2, "column": 4, "columnSpan": 1 },
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
        var card = Card(ScanBook);
        Assert.Equal(
            ["barcode", "cover", "sessions", "title"],
            Named(card).Select(c => c.Name!)
                .Where(n => n is "barcode" or "title" or "cover" or "sessions")
                .Distinct().Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(3, card.GetVisualDescendants().OfType<global::Avalonia.Controls.Button>().Count());
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
        var narrowCells = Cells(narrow).Where(c => c.Key != "sessions").ToList();

        var wide = Card(ScanBook.Replace(
            "\"key\": \"sessions\", \"row\": 4, \"column\": 1, \"columnSpan\": 4",
            "\"key\": \"sessions\", \"row\": 4, \"column\": 1, \"columnSpan\": 1",
            StringComparison.Ordinal));

        var list = Named(wide).Single(c => c.Name == "sessions");
        Assert.Equal(ColumnWidth, list.Bounds.Width, tolerance: 0.5);
        Assert.Equal(narrowCells, Cells(wide).Where(c => c.Key != "sessions").ToList());
    }

    private static List<(string Key, double Top, double Left)> Cells(SurfaceCard card) =>
        [.. Named(card).Select(c => (c.Name!, c.Bounds.Top, c.Bounds.Left))];
}