using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// A control can be the right width and still paint outside it, because its template has parts of its
/// own that refuse to shrink.
/// </summary>
/// <remarks>
/// <para><b>The defect this file exists for was found by asking a question the width tests could not
/// answer.</b> <c>FieldFillTests</c> proves the editing control's own <c>Bounds</c> equals the share the
/// row gave it — that is a claim about the box. The parts inside the box are laid out by the control's
/// template, and a template can demand more room than the box has. Measured at weight 1 of 10, where the
/// share is 38.2 pixels.</para>
///
/// <para><b>Two parts overflow and only one of them shows.</b> The <c>Select</c>'s Background border is
/// arranged 52 wide inside a 38.2 box, but it is a filled rectangle and the control clips — clipping a
/// fill to the box is what a box <em>does</em>, so nothing about it is visible or wrong. The drop-down
/// glyph is a different case: it is <em>shape</em>, so clipping it removes the right edge of the arrow,
/// and it sits at x=30..42 over a text area that runs to 38.2, so it also covers the end of a long
/// option. Both are recorded below; only the second is asserted as a defect.</para>
///
/// <para><b>Where the numbers came from.</b> The two spinner buttons of a <c>NumericUpDown</c> were 34
/// wide each — 68 inside a 38.2 box — and the <c>Select</c>'s Background was 64. A setter on the control
/// cannot reach either, because <c>MinWidth</c> on a <c>ComboBox</c> governs the <c>ComboBox</c>. The
/// <c>/template/</c> setters in <c>KitX.axaml</c> fixed both; what is left is the glyph's offset, which
/// is a position rather than a minimum and moving it is a separate decision.</para>
/// </remarks>
public sealed class FieldPartOverflowTests
{
    /// <summary>One part of a template that is laid out wider than the box its control was given.</summary>
    /// <param name="Kind">The part's name, or its type when unnamed.</param>
    /// <param name="OverBy">How far past the control's right edge, in device-independent pixels.</param>
    /// <param name="Visible">
    /// Whether the excess can be seen: <c>false</c> when the part is a fill that the control's own clip
    /// trims to the box, which is the correct result rather than a defect.
    /// </param>
    /// <param name="Note">What it means for the person looking at the card.</param>
    private sealed record Escapee(string Kind, double OverBy, bool Visible, string Note);

    /// <summary>
    /// Every part that is laid out wider than its control, at weight 1 of 10.
    /// </summary>
    /// <remarks>
    /// <b>These are measurements, not tolerances.</b> <c>Input</c> and <c>Switch</c> are absent because
    /// both are clean: their templates are a single TextBox and a CheckBox, and both accept the width
    /// they are given. A kind that is fixed later drops out of this table and the change-detector below
    /// says so.
    /// </remarks>
    private static readonly Dictionary<string, Escapee[]> Measured = new(StringComparer.Ordinal)
    {
        ["Input"] = [],
        ["Switch"] = [],
        ["Select"] =
        [
            new("Background", 13.8, false, "a fill: the control's clip trims it to the box, which is correct"),
            new("DropDownGlyph", 3.8, true, "the arrow's right edge is cut off, and it covers the text"),
        ],
        ["Number"] = [],
    };

    public static TheoryData<string> EditingKinds() => ["Input", "Number", "Select", "Switch"];

    [AvaloniaTheory]
    [MemberData(nameof(EditingKinds))]
    public void Nothing_that_shows_reaches_past_the_box_the_row_gave_it(string kind)
    {
        var visible = Escapes(kind).Where(e => e.Visible).ToArray();

        Assert.True(
            visible.Length == 0,
            $"{kind} at weight 1 of 10 draws {visible.Length} part(s) outside its box: "
            + string.Join("; ", visible.Select(e => $"{e.Kind} by {e.OverBy:F1}px ({e.Note})"))
            + ". The box is the right width -- FieldFillTests proves that -- so what is wrong is inside "
            + "the template, not the row arithmetic.");
    }

    /// <summary>
    /// The recorded table still describes what the templates do.
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="Measured"/> would be a comment with a dictionary around it: a fix that
    /// shortened a template would leave the table claiming the old number and only the failing test
    /// above would notice — and it would notice by going green, which reads as progress either way.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(EditingKinds))]
    public void The_recorded_overflow_is_what_the_template_does_today(string kind)
    {
        var measured = Escapes(kind).OrderBy(e => e.Kind, StringComparer.Ordinal).ToArray();
        var recorded = Measured[kind].OrderBy(e => e.Kind, StringComparer.Ordinal).ToArray();

        Assert.Equal(recorded.Select(e => (e.Kind, e.Visible)), measured.Select(e => (e.Kind, e.Visible)));

        // The widths compared with a tolerance: layout arithmetic produces 13.800000000000011 where the
        // decimal literal is 13.8, and an exact tuple comparison would fail on digits that no one chose.
        for (var i = 0; i < recorded.Length; i++)
        {
            Assert.True(
                Math.Abs(recorded[i].OverBy - measured[i].OverBy) < 0.05,
                $"{kind}'s {recorded[i].Kind} was recorded at {recorded[i].OverBy:F1}px and now measures "
                + $"{measured[i].OverBy:F1}px. Either the template changed or the recorded number was wrong; "
                + "both mean this table needs a decision, not a nudge.");
        }
    }

    /// <summary>
    /// The glyph and the text area of a <c>Select</c> do not sit on top of each other.
    /// </summary>
    /// <remarks>
    /// <para>This is the question fleet-delta asked when it said it could not read pixels: "is the
    /// drop-down arrow on top of a long option?" It does not need pixels. Both are laid-out controls
    /// with rectangles, and the answer at a narrow width is in those rectangles.</para>
    ///
    /// <para>At 38.2 the content area runs 0..38.2 and the glyph 30..42, so they overlap by 8.2 — which
    /// is why this fails today. It is kept as its own assertion because a fix that moved the glyph left
    /// would satisfy the "nothing visible overflows" test while making this overlap worse.</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_selects_drop_down_glyph_does_not_sit_on_top_of_its_text()
    {
        var (glyph, content) = SelectParts();

        var overlap = content.Right - glyph.X;

        Assert.True(
            overlap <= 0,
            $"the drop-down glyph starts at x={glyph.X:F1} while the text area runs to x={content.Right:F1}, "
            + $"so they overlap by {overlap:F1}px. A long option would be drawn under the arrow, and the "
            + $"arrow itself is clipped by the control's {content.Right:F1}px box.");
    }

    /// <summary>The parts a <c>Select</c> is made of, laid out at weight 1 of 10.</summary>
    private static (Rect Glyph, Rect Content) SelectParts()
    {
        var card = Layout(Element("Select", weight: 1), siblings: 9);

        var combo = card.GetVisualDescendants().OfType<ComboBox>().Single();

        var glyph = combo.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "DropDownGlyph");
        var content = combo.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_ContentPresenter");

        return (glyph.Bounds, content.Bounds);
    }

    /// <summary>Every template part laid out wider than its control's box, at weight 1 of 10.</summary>
    private static List<Escapee> Escapes(string kind)
    {
        var card = Layout(Element(kind, weight: 1), siblings: 9);

        // The outermost editing control: NumericUpDown contains a TextBox, so "any of the four kinds"
        // is not unique and picking the innermost would measure the wrong box.
        bool IsPlaced(Control c) => c.GetVisualAncestors()
            .OfType<Control>()
            .All(a => a is not (TextBox or NumericUpDown or ComboBox or CheckBox));

        var control = card.GetVisualDescendants()
            .OfType<Control>()
            .Single(c => c is TextBox or NumericUpDown or ComboBox or CheckBox && IsPlaced(c));

        var width = control.Bounds.Width;
        var found = new List<Escapee>();

        foreach (var part in control.GetVisualDescendants().OfType<Control>())
        {
            // A Path inside a Viewbox reports the glyph's own coordinate space — thousands of pixels
            // — which is a fact about the path data, not about where the icon lands. Both are matched
            // by name because `ViewboxContainer` is internal to Avalonia and cannot be named here.
            var typeName = part.GetType().Name;
            if (typeName is "Path" or "ViewboxContainer" or "Popup")
                continue;

            if (part.Bounds.Width <= 0)
                continue;

            var over = part.Bounds.Right - width;
            if (over <= 0.5)
                continue;

            // A Border with no child is a fill: the control clips it to the box and the result is what
            // a box is for. Anything with content loses some of that content when it is clipped.
            var isFill = part is Border { Child: null };

            found.Add(new Escapee(
                part.Name ?? typeName,
                over,
                Visible: !isFill,
                Note: isFill ? "a fill: trimmed to the box, which is correct" : "its content is cut off"));
        }

        return found;
    }

    private static SurfaceElement Element(string kind, int weight) => kind switch
    {
        "Input" => new SurfaceInputElement { Row = 1, Weight = weight, Name = "c" },
        "Number" => new SurfaceNumberElement { Row = 1, Weight = weight, Name = "c" },
        "Select" => new SurfaceSelectElement { Row = 1, Weight = weight, Name = "c" },
        "Switch" => new SurfaceSwitchElement { Row = 1, Weight = weight, Name = "c" },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not an editing kind"),
    };

    private static SurfaceCard Layout(SurfaceElement element, int siblings)
    {
        var card = new SurfaceCard();
        card.SetDocument(new SurfaceDocument
        {
            Elements = [new SurfaceTextElement { Row = 1, Weight = siblings, Key = "wide" }, element],
        });

        var window = new Window
        {
            Content = card,
            Width = SurfaceMetrics.CardWidth,
            Height = SurfaceMetrics.CardHeight,
        };

        // The product theme, the way a host loads it. Without it the control is a bare Fluent one and
        // these numbers belong to a different product.
        var include = new StyleInclude(new Uri("avares://KitX.ToolKit.View.Avalonia/"))
        {
            Source = new Uri("avares://KitX.ToolKit.View.Avalonia/Themes/KitX.axaml"),
        };
        _ = include.Loaded;
        window.Styles.Add(include);

        window.Show();
        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        return card;
    }
}
