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
/// A Select or a Switch has to fill the share its row gave it, not the width its own template prefers.
/// </summary>
/// <remarks>
/// <para><b>What this exists to catch.</b> Every layout test in this project measures positions and
/// slot heights; none of them asked whether an editing control occupies its column. Measured on four
/// kinds alone in a 390px row: <c>TextBox</c> and <c>NumericUpDown</c> stretch to 390 by themselves,
/// <c>ComboBox</c> takes 64 and <c>CheckBox</c> takes 28 — because those two carry their own natural
/// width and only stretch when something tells them to. What tells them to is
/// <c>HorizontalAlignment = Stretch</c> in <c>Themes/KitX.axaml</c>, under the
/// <c>ComboBox.kitx-field</c> / <c>CheckBox.kitx-field</c> selectors.</para>
///
/// <para><b>So this test's subject is the theme as much as the renderer.</b> Run against a host that
/// loads only <c>FluentTheme</c>, it reads 64 and 28 and fails; add the product theme and they become
/// the row's width. That makes it a guard over the stylesheet, which nothing else in the suite
/// covers: <c>ThemedClassesTests</c> checks that every class the renderer applies has a selector, and
/// that is a check about names, not about what the selectors do.</para>
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
