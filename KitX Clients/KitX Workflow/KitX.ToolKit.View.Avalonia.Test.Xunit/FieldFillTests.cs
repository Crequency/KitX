using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// An editing control has to occupy the share its row gave it — no more, and no less.
/// </summary>
/// <remarks>
/// <para><b>Two failures, one cause, and only one of them was visible.</b> Every layout test in this
/// project measures positions and slot heights; none asked whether an editing control occupies its
/// column. Measured with the product theme and no <c>MinWidth</c> override:</para>
///
/// <code>
/// alone in a 390px row:   TextBox 390   NumericUpDown 390   ComboBox 390   CheckBox 390
/// at weight 1 of 9:       ComboBox 38.2   CheckBox 38.2   TextBox 64   NumericUpDown 64
/// </code>
///
/// <para>The second row is the visible failure: 38 pixels of column, 64 pixels of control, 26
/// pixels painted over the neighbour. The fix — <c>MinWidth = 0</c> on the field styles — repairs
/// both, because a minimum larger than the column is what stops <c>Stretch</c> from taking effect at
/// all. Both ends are asserted here, since a fix with two consequences needs two assertions.</para>
///
/// <para><b>Why this is a test of the stylesheet.</b> <c>HorizontalAlignment = Stretch</c> lives in
/// <c>Themes/KitX.axaml</c>. <c>ThemedClassesTests</c> checks that every class the renderer applies
/// has a selector, which is a check about names rather than about what the selectors do. Nothing
/// else in the suite would notice a selector being emptied.</para>
///
/// <para><b>The class comment above describes a third state, and it is worth separating.</b> Against
/// a host that loads only <c>FluentTheme</c>, the same four read 390 / 390 / <b>64</b> / <b>28</b>
/// even alone in a row — because the product's stretch setters are not loaded at all. That is not
/// the defect fixed here; it is what happens when the host is not the shipped product.</para>
///
/// <para><b>Deleting <c>window.Styles.Add(Theme())</c> fails five of these eight cases.</b> Measured,
/// because the first version of this paragraph claimed the opposite — that the file would go green
/// while measuring a control the product does not have. It does not: <c>Select</c> alone reads 64
/// instead of 390, <c>Switch</c> alone reads 28, and the same two plus <c>Input</c> and
/// <c>Number</c> miss their share in the 9:1 row. The include is load-bearing, and removing it is
/// loud. What would <em>not</em> be loud is a host that loaded some <c>Stretch</c> setters from
/// somewhere else: the widths would come out right for the wrong reason, and only a reader of this
/// comment would know the difference.</para>
///
/// <para><b>Numbers here are relationships, not constants.</b> The assertion is "the control takes
/// the share the arithmetic gives it, within a pixel", computed from <see cref="SurfaceMetrics"/>
/// each time. A test that hardcoded 38 would have to be edited whenever the card width changed, and
/// the edit would be made by whoever wanted it green.</para>
/// </remarks>
public sealed class FieldFillTests
{
    /// <summary>Every kind that a person types into or chooses from.</summary>
    public static TheoryData<string> EditingKinds() => ["Input", "Number", "Select", "Switch"];

    [AvaloniaTheory]
    [MemberData(nameof(EditingKinds))]
    public void An_editing_control_alone_in_a_row_fills_the_row(string kind)
    {
        var card = Card(Element(kind, weight: 1));

        var control = CardProbe.Elements(card).Single();

        // §Q102: one element in a row is weight 1/1, and 1/1 means the whole row.
        Assert.Equal(
            SurfaceMetrics.CardWidth,
            control.Bounds.Width,
            tolerance: 0.5);
    }

    [AvaloniaTheory]
    [MemberData(nameof(EditingKinds))]
    public void An_editing_control_at_weight_one_beside_a_heavy_sibling_takes_its_share(string kind)
    {
        var card = Card(
            new SurfaceTextElement { Row = 1, Weight = 9, Key = "wide" },
            Element(kind, weight: 1));

        var control = CardProbe.Elements(card).Last();

        // One part out of ten, after the gap is taken out: (390 - 8) / 10.
        var expected = (SurfaceMetrics.CardWidth - SurfaceMetrics.FieldGap) / 10.0;

        Assert.Equal(expected, control.Bounds.Width, tolerance: 0.5);
    }

    private static SurfaceElement Element(string kind, int weight) => kind switch
    {
        "Input" => new SurfaceInputElement { Row = 1, Weight = weight, Name = "input" },
        "Number" => new SurfaceNumberElement { Row = 1, Weight = weight, Name = "number" },
        "Select" => new SurfaceSelectElement { Row = 1, Weight = weight, Name = "select" },
        "Switch" => new SurfaceSwitchElement { Row = 1, Weight = weight, Name = "switch" },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not an editing kind"),
    };

    private static SurfaceCard Card(params SurfaceElement[] elements)
    {
        var card = new SurfaceCard();
        card.SetDocument(new SurfaceDocument { Elements = elements });

        var window = new Window
        {
            Content = card,
            Width = SurfaceMetrics.CardWidth,
            Height = SurfaceMetrics.CardHeight,
        };

        // The product theme, not only the platform one. This is the whole test: `Stretch` for
        // ComboBox and CheckBox lives in Themes/KitX.axaml, so a host without the include measures a
        // bare ComboBox — 64px — and would be measuring a different product than the one shipped.
        window.Styles.Add(Theme());

        window.Show();
        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        return card;
    }

    /// <summary>Reads the theme the renderer ships, the way a host would name it.</summary>
    private static StyleInclude Theme()
    {
        var include = new StyleInclude(new Uri("avares://KitX.ToolKit.View.Avalonia/"))
        {
            Source = new Uri("avares://KitX.ToolKit.View.Avalonia/Themes/KitX.axaml"),
        };

        // Instantiating the include is what reads the file; an unloaded one contributes nothing.
        _ = include.Loaded;

        return include;
    }
}
