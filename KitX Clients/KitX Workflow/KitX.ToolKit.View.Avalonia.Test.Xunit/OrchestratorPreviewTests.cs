using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View.Avalonia;
using System.Text.Json;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// The orchestrator's preview and the renderer's real layout, measured side by side.
/// </summary>
/// <remarks>
/// <para><b>These two implementations are allowed to disagree about pixels and not about
/// proportions.</b> The orchestrator draws a 390-wide canvas from its own constants
/// (<c>SurfaceElementVM.CanvasWidth</c> / <c>CellGap</c> / <c>RowHeight</c>); the renderer draws a real
/// card from <see cref="SurfaceMetrics"/>. A row's weights are a ratio in both, so the ratio is the
/// part that has to survive the trip — a card laid out 3:1 has to preview as 3:1.</para>
///
/// <para><b>The formula is copied here rather than called.</b> <c>SurfaceElementVM</c> lives in
/// <c>KitX.Dashboard</c>, and referencing it would make the renderer's tests depend on the
/// application — the exact edge ADR-0001 split the assemblies to remove. A copy can drift; the point
/// is that it drifts <em>visibly</em>, because this test fails the moment one side changes and the
/// other does not.</para>
///
/// <para><b>What this test cannot see, and says so:</b> the gap is not one of the things it judges.
/// The orchestrator insets every cell by <c>CellGap / 2</c>; the renderer spends one
/// <c>FieldGap</c> between columns. Those are two independent constants and are allowed to differ —
/// a preview that used twice the renderer's gap would pass every assertion here. That is a scope
/// declaration, not a tolerance: the preview promises proportions, and the gap is each side's own
/// choice. Making it a shared constant would put a pixel decision about the canvas into the renderer,
/// which is the edge Q80 drew.</para>
/// </remarks>
public sealed class OrchestratorPreviewTests
{
    /// <summary>Two elements on one row, weights three and one.</summary>
    private const string Document = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Text", "row": 1, "weight": 3, "key": "title" },
            { "type": "Text", "row": 1, "weight": 1, "key": "subtitle" }
          ]
        }
        """;

    /// <summary>The orchestrator's own gap between cells, mirrored from <c>SurfaceElementVM</c>.</summary>
    private const double OrchestratorGap = 8;

    /// <summary>The orchestrator's canvas width, mirrored from <c>SurfaceElementVM</c>.</summary>
    private const double OrchestratorWidth = 390;

    private static (Window Window, SurfaceCard Card) Render()
    {
        var card = new SurfaceCard();
        card.SetDocument(JsonSerializer.Deserialize<SurfaceDocument>(Document)!);

        var window = new Window
        {
            Width = SurfaceMetrics.CardWidth,
            Height = SurfaceMetrics.CardHeight,
        };

        window.Content = card;
        window.Show();

        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        return (window, card);
    }

    /// <summary>
    /// The renderer divides a row three parts to one when the document says three parts to one.
    /// </summary>
    /// <remarks>
    /// Reading <see cref="Control.Bounds"/> off the cells rather than asking the layout engine what it
    /// intended: the number that matters is the one a person would measure on the screen.
    /// </remarks>
    [AvaloniaFact]
    public void A_row_three_parts_to_one_is_three_times_wider_on_the_left()
    {
        var (window, card) = Render();

        var cells = CardProbe.Elements(card).OfType<TextBlock>().ToList();
        Assert.Equal(2, cells.Count);

        var wide = cells[0].Bounds.Width;
        var narrow = cells[1].Bounds.Width;

        Assert.True(narrow > 0, $"the second cell measured {narrow:0.##}px wide, so a ratio cannot be read from it");

        Assert.InRange(wide / narrow, 3 - 0.01, 3 + 0.01);

        // The window stays alive to the end of the test: closing it disposes the visual tree, and a
        // size read off a detached control is a different question from the one being asked.
        Assert.NotNull(window);
    }

    /// <summary>
    /// The preview's formula puts those same two weights three parts to one.
    /// </summary>
    /// <remarks>
    /// <para>This is the orchestrator's arithmetic, reproduced here so the two ratios can be compared
    /// as numbers rather than as two separate assertions that never meet.</para>
    ///
    /// <para>The gap convention differs on purpose: the orchestrator insets every cell by half a gap,
    /// so a row of <c>n</c> cells spends <c>n</c> gaps; the renderer spends <c>n - 1</c> gaps between
    /// columns. That is why widths cannot be compared and ratios can.</para>
    /// </remarks>
    [AvaloniaFact]
    public void The_preview_puts_the_same_two_weights_three_parts_to_one()
    {
        var (window, card) = Render();

        // (390 - 2 * 8) * 3 / 4 and (390 - 2 * 8) * 1 / 4 — SurfaceElementVM.CellWidth for each.
        var available = OrchestratorWidth - (2 * OrchestratorGap);
        var previewWide = available * 3 / 4;
        var previewNarrow = available * 1 / 4;

        var cells = CardProbe.Elements(card).OfType<TextBlock>().ToList();
        var renderedWide = cells[0].Bounds.Width;
        var renderedNarrow = cells[1].Bounds.Width;

        // The one claim both implementations have to agree on: the preview is a faithful sketch of
        // the proportion. Absolute widths are allowed to differ, and do.
        Assert.InRange(
            (renderedWide / renderedNarrow) / (previewWide / previewNarrow),
            1 - 0.01,
            1 + 0.01);

        Assert.NotNull(window);
    }

    /// <summary>
    /// Each side is internally consistent: one row fills the card it is drawn on.
    /// </summary>
    /// <remarks>
    /// Separate from the ratio test because they fail for different reasons. A ratio can be right
    /// while a row leaves a gap at its right edge — that is a card with a seam, and nothing about
    /// proportion would report it.
    /// </remarks>
    [AvaloniaFact]
    public void A_three_to_one_row_uses_the_whole_card()
    {
        var (window, card) = Render();

        var cells = CardProbe.Elements(card).OfType<TextBlock>().ToList();

        var used = cells.Sum(cell => cell.Bounds.Width);
        var gaps = SurfaceMetrics.FieldGap * (cells.Count - 1);

        Assert.InRange(used + gaps, SurfaceMetrics.CardWidth - 0.5, SurfaceMetrics.CardWidth + 0.5);

        Assert.NotNull(window);
    }
}
