using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Media;
using global::Avalonia.Styling;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View.Avalonia;
using System.Text.Json;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// The theme, put in front of a control for the first time.
/// </summary>
/// <remarks>
/// <para><b>Nothing in this project had ever rendered a themed card.</b> <see cref="ThemedClassesTests"/>
/// reads the theme file as data and proves every class it names has a selector;
/// <see cref="UnthemedCardTests"/> proves a card survives without it. Neither puts a <c>Style</c>
/// between the two, so "the selector exists" and "the selector took effect" were the same claim by
/// default rather than by test.</para>
///
/// <para><b>This is the only test that fails when a selector names the wrong control type.</b> A
/// selector reading <c>Border.kitx-danger</c> while the renderer produces a <c>Button</c> passes the
/// class test — that one compares strings — and passes the unthemed test — that one asserts absence.
/// Only rendering the card and reading the brush back can tell them apart.</para>
/// </remarks>
public sealed class ThemedCardTests
{
    private const string ThemeUri = "avares://KitX.ToolKit.View.Avalonia/Themes/KitX.axaml";

    private const string Document = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Button", "row": 1, "weight": 1, "label": "Save", "events": ["Save"] },
            { "type": "Button", "row": 2, "weight": 1, "label": "End", "events": ["End"], "emphasis": "danger" }
          ]
        }
        """;

    /// <summary>
    /// The same card rendered twice — once in a window carrying the shipped theme, once in one that
    /// carries only the Fluent theme.
    /// </summary>
    /// <remarks>
    /// Two windows rather than one window measured twice: switching styles on a live control does not
    /// guarantee the styling pass runs again, and a stale brush would make the comparison pass for the
    /// wrong reason.
    /// </remarks>
    private static (Button Themed, Button Plain) Buttons()
    {
        var (themedWindow, themedCard) = Render(withTheme: true);
        var (plainWindow, plainCard) = Render(withTheme: false);

        var themed = DangerButton(themedCard);
        var plain = DangerButton(plainCard);

        // Both windows stay alive until the end of the test: closing one disposes its visual tree, and
        // a brush read off a detached control is a different question from the one being asked.
        Assert.NotNull(themedWindow);
        Assert.NotNull(plainWindow);

        return (themed, plain);
    }

    private static (Window Window, SurfaceCard Card) Render(bool withTheme)
    {
        var card = new SurfaceCard();
        card.SetDocument(JsonSerializer.Deserialize<SurfaceDocument>(Document)!);

        var window = new Window { Width = SurfaceMetrics.CardWidth, Height = SurfaceMetrics.CardHeight };

        if (withTheme)
            window.Styles.Add(Theme());

        window.Content = card;
        window.Show();

        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        return (window, card);
    }

    /// <summary>Reads the theme the renderer ships, the way a host would name it.</summary>
    private static Styles Theme()
    {
        var include = new StyleInclude(new Uri("avares://KitX.ToolKit.View.Avalonia/"))
        {
            Source = new Uri(ThemeUri),
        };

        // Instantiating the include is what reads the file; an unloaded one contributes nothing.
        _ = include.Loaded;

        return new Styles { include };
    }

    private static Button DangerButton(SurfaceCard card) =>
        CardProbe.Elements(card)
            .OfType<Button>()
            .Single(button => button.Classes.Contains("kitx-danger"));

    /// <summary>
    /// The dangerous button's background differs once the theme is in the window.
    /// </summary>
    /// <remarks>
    /// <para><b>Comparing two renders instead of naming a colour.</b> Asserting "it is #FDF3F3" would
    /// pin the test to one value of a resource a host is explicitly allowed to replace — the theme's
    /// whole extension point is "define the same colour key and yours wins". What is being tested is
    /// that the selector reaches this control at all, and that is what inequality says.</para>
    ///
    /// <para><b>Both sides must be non-null and different.</b> A themed button with the theme's
    /// background and an unthemed one with nothing are different; two nulls would be equal, and that
    /// is the failure this rules out — the theme silently not loading would leave both sides plain.</para>
    /// </remarks>
    [AvaloniaFact]
    public void The_theme_changes_what_the_danger_button_looks_like()
    {
        var (themed, plain) = Buttons();

        Assert.NotNull(themed.Background);
        Assert.NotEqual(themed.Background, plain.Background);
    }

    /// <summary>
    /// The ordinary button and the dangerous one do not end up with the same brush.
    /// </summary>
    /// <remarks>
    /// This catches the other half of a mistyped selector: one that matches too much. A selector
    /// written as <c>Button</c> alone would style every button, and the test above would still pass
    /// because the themed and unthemed renders would still differ. What separates the two is that
    /// <c>normal</c> and <c>danger</c> are supposed to read differently from each other.
    /// </remarks>
    [AvaloniaFact]
    public void Emphasis_keeps_two_buttons_on_one_card_apart_once_the_theme_is_in()
    {
        var (_, card) = Render(withTheme: true);

        var buttons = CardProbe.Elements(card).OfType<Button>().ToList();

        var normal = buttons.Single(button => ClassesOf(button).Contains("kitx-button"));
        var danger = buttons.Single(button => ClassesOf(button).Contains("kitx-danger"));

        Assert.NotEqual(normal.Background, danger.Background);
    }

    private static string Describe(object? brush) => brush switch
    {
        null => "null",
        ISolidColorBrush solid => "#" + solid.Color.ToString(),
        _ => brush.GetType().Name,
    };

    private static IEnumerable<string> ClassesOf(Control control) =>
        control.Classes.Where(name => !name.StartsWith(':'));
}
