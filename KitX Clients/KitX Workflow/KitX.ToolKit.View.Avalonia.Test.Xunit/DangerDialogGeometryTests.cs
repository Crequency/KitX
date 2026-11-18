using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Styling;
using global::Avalonia.VisualTree;
using KitX.ToolKit.Contract.Surface;
using System.Text.Json;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// The confirmation dialog has a size and a place, not just a behaviour.
/// </summary>
/// <remarks>
/// <para><b>Why this is a separate file from <c>DangerConfirmTests</c>.</b> That class asks whether a
/// dangerous click asks first, and every one of its assertions is about events and closing. It could
/// not tell you whether the dialog is 288 wide or whether it sits in the middle of the card — and for
/// one release it did not: the width and padding were changed with nothing but the Shots PNGs as
/// evidence that anything had moved. A screenshot says <i>something changed</i>; it does not say
/// <i>what it changed to</i>, and it only says either when someone remembers to re-render.</para>
///
/// <para><b>One window for the whole class</b>, as in the file next door, and it carries the theme:
/// creating a window per case is expensive under headless and two windows interact through
/// <c>UpdateLayout</c> scheduling. The theme is loaded here rather than there because adding styles to
/// that class's shared host would change what its colour assertions read.</para>
///
/// <para><b>What this file cannot measure, and why it says so instead of measuring it.</b> The height
/// is not asserted. Under <c>UseHeadlessDrawing</c> the font metrics are stubs, so the dialog lays out
/// shorter than it does in the product; an assertion on its height here would be an assertion about
/// the stub. The three readings and their conditions:</para>
/// <list type="bullet">
/// <item><description><c>288x115</c> — the Shots renderer, real Skia, theme loaded. The true value,
/// confirmed independently by the PNG's white block spanning 113 px plus a border pixel each side.</description></item>
/// <item><description><c>288x91</c> — an <c>AvaloniaFact</c> with the theme, headless font metrics as
/// stubs. Font sizes are right (18/13), line heights are not (15 vs 25, 11 vs 18).</description></item>
/// <item><description><c>288x99</c> — an <c>AvaloniaFact</c> <i>without</i> the theme. Meaningless:
/// every font size in it is the default 14, which is the shape of "the theme never reached it".</description></item>
/// </list>
/// <para>The last two are both wrong for different reasons, which is why they are written out
/// together. Quoting only "headless measures 91" would let the next reader take 91 as a small
/// deviation from 115 rather than as a different object.</para>
/// </remarks>
public sealed class DangerDialogGeometryTests
{
    private const string Document = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Text",   "key": "title", "row": 1, "weight": 1 },
            { "type": "Button", "label": "End the batch", "events": ["End"], "emphasis": "danger", "row": 2, "weight": 1 }
          ]
        }
        """;

    /// <summary>Opens the dialog on a fresh card and returns both, since the assertions need both.</summary>
    private static (SurfaceCard Card, Border Dialog) Open()
    {
        var card = new SurfaceCard();
        card.SetDocument(JsonSerializer.Deserialize<SurfaceDocument>(Document)!);

        var window = Host;
        window.Content = card;

        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        CardProbe.Elements(card).OfType<Button>().Single(b => (string?)b.Content == "End the batch")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        window.UpdateLayout();

        var dialog = card.GetVisualDescendants()
            .OfType<Border>()
            .Single(b => b.Classes.Contains(SurfaceCard.ThemedDialog));

        return (card, dialog);
    }

    private static Window Host
    {
        get
        {
            if (_host is not null)
                return _host;

            _host = new Window { Width = SurfaceMetrics.CardWidth, Height = SurfaceMetrics.CardHeight };

            _host.Styles.Add(new Styles { Theme() });

            _host.Show();

            return _host;
        }
    }

    private static Window? _host;

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

    private static Panel Scrim(SurfaceCard card) =>
        card.Children.OfType<Panel>().Single(p => p.Classes.Contains(SurfaceCard.ThemedScrim));

    [AvaloniaFact]
    public void The_dialog_is_a_fixed_width_one_screen_wide_card_worth_of_centred()
    {
        var (card, dialog) = Open();

        // Equality, not "at least". A `>=` would pass a dialog narrowed to 200, and the only thing a
        // lower bound buys — not having to touch the test when it widens — is exactly the moment
        // somebody should be looking at the picture again.
        Assert.Equal(288, dialog.Bounds.Width);

        // The centring sum compares two numbers in the same coordinate system, so the premise that
        // they are in the same system is asserted rather than assumed. The dialog's Bounds are
        // measured against the scrim; the scrim is asserted to be the card's width and its left edge
        // to be the card's, which is what makes `dialog.Bounds.X` comparable with `card.Bounds.Width`.
        var scrim = Scrim(card);

        Assert.Equal(card.Bounds.Width, scrim.Bounds.Width);
        Assert.Equal(0, scrim.Bounds.X);

        var centred = (card.Bounds.Width - dialog.Bounds.Width) / 2;

        Assert.True(Math.Abs(centred - dialog.Bounds.X) <= 1,
            $"the dialog is {dialog.Bounds.Width} wide inside a {card.Bounds.Width} wide card, so it " +
            $"belongs at x={centred}, and it is at x={dialog.Bounds.X}");
    }
}
