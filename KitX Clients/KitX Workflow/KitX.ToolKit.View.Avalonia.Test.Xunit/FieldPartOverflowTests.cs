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
/// share is 38.2 pixels:</para>
///
/// <code>
/// Select        content x=0.0..38.2   drop-down glyph x=30.0..42.0   glyph extends 3.8px past the box
///               and overlaps the content area by 8.2px -- only 8.2 of its 12 pixels are on the card
/// NumericUpDown PART_DecreaseButton   x=34..68                       extends 29.8px past the box
///               PART_TextBox            x=-1..1                       1px wide, inside a 38.2px box
/// ComboBox      Background border     x=-6..58                       extends 19.8px past the box
/// </code>
///
/// <para><b>The four kinds do not fail the same way</b>, which is the reason this is asserted per kind
/// rather than as "nothing sticks out". <c>Input</c> and <c>Switch</c> are clean at this width — their
/// templates are a single TextBox and a CheckBox and both accept the width they are given.
/// <c>Select</c> is off by 3.8; <c>NumericUpDown</c> is off by 29.8. Collapsing them into one number
/// would report 29.8 for the file and say nothing about the drop-down arrow.</para>
///
/// <para><b>Why this measures the template and not the theme.</b> <c>MinWidth = 0</c> on the field
/// styles (the fix for the twelfth defect) governs the control's own minimum. These parts are not
/// governed by that setter: a <c>NumericUpDown</c>'s spinner buttons have their own width, and a
/// <c>ComboBox</c>'s glyph sits at a fixed offset from the right edge. So the same fix cannot reach
/// them, and a test that only measures the outer <c>Bounds</c> cannot see them.</para>
/// </remarks>
public sealed class FieldPartOverflowTests
{
    /// <summary>One part of a template that reaches outside the box the row gave the control.</summary>
    /// <param name="OverBy">How far past the right edge, in device-independent pixels.</param>
    /// <param name="Note">What it means for the person looking at the card.</param>
    private sealed record Escapee(string Kind, double OverBy, string Note);

    /// <summary>
    /// How far each kind's template parts reach past their own box, at weight 1 of 10.
    /// </summary>
    /// <remarks>
    /// <b>These numbers are measurements, not tolerances.</b> Each one was read off a laid-out control
    /// with the product theme loaded. The three non-empty rows are the twelfth defect's second half:
    /// the outer box is correct (38.2, by <c>FieldFillTests</c>) while parts inside it are still sized
    /// by the 64 they were born with.
    ///
    /// <para><c>Select</c> escapes twice and the two are different facts. <c>Background</c> is 64 wide,
    /// which is the same inherited minimum that made the outer box overflow before it was set to zero —
    /// so the fix reached the control but not its background border. <c>DropDownGlyph</c> is 12 wide at
    /// x=30, which is a fixed offset from the right edge and therefore its own problem.</para>
    /// </remarks>
    private static readonly Dictionary<string, Escapee[]> Measured = new(StringComparer.Ordinal)
    {
        ["Input"] = [],
        ["Switch"] = [],
        ["Select"] =
        [
            new("Background", 19.8, "the background border is still 64 wide"),
            new("DropDownGlyph", 3.8, "the arrow is cut off at the right edge"),
        ],
        ["Number"] = [new("PART_DecreaseButton", 29.8, "the spinner buttons are drawn outside the cell")],
    };

    public static TheoryData<string> EditingKinds() => ["Input", "Number", "Select", "Switch"];

    [AvaloniaTheory]
    [MemberData(nameof(EditingKinds))]
    public void No_part_of_a_template_reaches_past_the_box_the_row_gave_it(string kind)
    {
        var escapes = Escapes(kind);

        Assert.True(
            escapes.Count == 0,
            $"{kind} at weight 1 of 10 has {escapes.Count} part(s) outside its box: "
            + string.Join(
                "; ",
                escapes.Select(e => $"{e.Kind} by {e.OverBy:F1}px ({e.Note})"))
            + ". The box is the right width -- FieldFillTests proves that -- so what is wrong is the "
            + "template's own minimum, not the row arithmetic.");
    }

    /// <summary>
    /// The recorded table still describes what the code does.
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="Measured"/> would be a comment with a dictionary around it: a fix that
    /// shortened a template would leave the table claiming the old number and nothing would notice.
    /// </remarks>
    [AvaloniaTheory]
    [MemberData(nameof(EditingKinds))]
    public void The_recorded_overflow_is_what_the_template_does_today(string kind)
    {
        var measured = Escapes(kind)
            .Select(e => (e.Kind, e.OverBy))
            .OrderBy(e => e.Kind, StringComparer.Ordinal)
            .ToArray();

        var recorded = Measured[kind]
            .Select(e => (e.Kind, e.OverBy))
            .OrderBy(e => e.Kind, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(recorded, measured);
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
    /// would satisfy the "nothing outside the box" test while making this overlap worse.</para>
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
            + $"arrow itself is clipped by the control's {(content.Right):F1}px box.");
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

    /// <summary>Every template part that reaches outside its control's box, at weight 1 of 10.</summary>
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
            // — which is a fact about the path data, not about where the icon lands. Only the box the
            // icon is drawn in says whether it fits. Both are matched by name because
            // `ViewboxContainer` is internal to Avalonia and cannot be named here.
            var typeName = part.GetType().Name;
            if (typeName is "Path" or "ViewboxContainer" or "Popup")
                continue;

            if (part.Bounds.Width <= 0)
                continue;

            var over = part.Bounds.Right - width;
            if (over > 0.5)
                found.Add(new Escapee(part.Name ?? typeName, over, "reaches past the control"));
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
        // the numbers above belong to a different product.
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
