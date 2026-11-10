using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using global::Avalonia.Styling;
using global::Avalonia.VisualTree;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.View.Avalonia;
using System.Text.Json;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// What a card does when the host never included the theme.
/// </summary>
/// <remarks>
/// <para><b>This state was always true and nobody had written it down.</b> Every test in this project
/// renders into a window whose styles carry <see cref="FluentTheme"/> and nothing else — the theme file
/// is read only by the two class tests, which read it as data and never put it in front of a control.
/// So the whole suite has been exercising the unthemed card all along, and "what happens when the theme
/// is missing" was an answer nobody had asked for out loud.</para>
///
/// <para><b>An unrecorded known behaviour and a forgotten one look identical next round.</b> That is the
/// whole reason these three assertions exist: not to change what happens, but to make it a decision.</para>
/// </remarks>
public sealed class UnthemedCardTests
{
    /// <summary>
    /// Seven element kinds, one of each, with a danger button to open the overlay with.
    /// </summary>
    private const string EveryKind = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Text", "row": 1, "weight": 1, "key": "title" },
            { "type": "Input", "row": 2, "weight": 1, "name": "note" },
            { "type": "Number", "row": 3, "weight": 1, "name": "count" },
            { "type": "Select", "row": 4, "weight": 1, "name": "mode", "options": ["a", "b"] },
            { "type": "Switch", "row": 5, "weight": 1, "name": "flag" },
            { "type": "Button", "row": 6, "weight": 1, "label": "Save", "events": ["Save"] },
            { "type": "Button", "row": 6, "weight": 1, "label": "End", "events": ["End"], "emphasis": "danger" },
            { "type": "List", "row": 7, "weight": 1, "key": "rows" }
          ]
        }
        """;

    /// <summary>
    /// A card in a window that carries nothing but the Fluent theme — no renderer theme anywhere.
    /// </summary>
    /// <remarks>
    /// Deliberately not using the shared host from the other files: that one is set up by tests whose
    /// subject is layout, and if somebody ever adds a <c>StyleInclude</c> there this file would quietly
    /// stop testing what it says it tests. A window built here cannot drift into having a theme.
    /// </remarks>
    private static SurfaceCard Unthemed(out Window window)
    {
        var card = new SurfaceCard();
        card.SetDocument(JsonSerializer.Deserialize<SurfaceDocument>(EveryKind)!);

        window = new Window { Width = SurfaceMetrics.CardWidth, Height = SurfaceMetrics.CardHeight };
        window.Content = card;
        window.Show();

        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        return card;
    }

    private static Panel? Overlay(SurfaceCard card) =>
        card.Children.OfType<Panel>().SingleOrDefault(p => p.Classes.Contains(SurfaceCard.ThemedScrim));

    private static Button ButtonByLabel(SurfaceCard card, string label) =>
        CardProbe.Elements(card).OfType<Button>().Single(b => (string?)b.Content == label);

    /// <summary>
    /// Every element reaches a control with real geometry even though no selector anywhere names it.
    /// </summary>
    /// <remarks>
    /// The failure this rules out is not "the colours are missing" — it is "a card with no styles throws
    /// or lays out to nothing", which would make the renderer unusable in any host that forgot one line.
    /// Geometry rather than mere existence: a control of zero height is present and useless.
    /// </remarks>
    [AvaloniaFact]
    public void A_card_without_the_theme_still_lays_out_every_element_it_was_given()
    {
        var card = Unthemed(out _);

        var elements = CardProbe.Elements(card);

        Assert.Equal(8, elements.Count);
        Assert.All(elements, element => Assert.True(element.Bounds.Height > 0, $"{element.GetType().Name} got no height"));
        Assert.All(elements, element => Assert.True(element.Bounds.Width > 0, $"{element.GetType().Name} got no width"));
    }

    /// <summary>
    /// The confirmation still says what it is asking, because the words fall back to English.
    /// </summary>
    /// <remarks>
    /// A dialog with no words is worse than a card with no colours: the person is asked to approve
    /// something and cannot tell what. That is why text falls back and colour does not, and this is the
    /// assertion that records the difference.
    /// </remarks>
    [AvaloniaFact]
    public void A_dialog_without_the_theme_still_says_what_it_is_asking()
    {
        var card = Unthemed(out _);

        ButtonByLabel(card, "End").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var overlay = Overlay(card);

        Assert.NotNull(overlay);

        var words = overlay!.GetVisualDescendants()
            .OfType<Control>()
            .Select(control => control switch
            {
                TextBlock block => block.Text,
                Button button => button.Content as string,
                _ => null,
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        Assert.Contains("Are you sure?", words);
        Assert.Contains("Cancel", words);
        Assert.Contains("Continue", words);
    }

    /// <summary>
    /// Without the theme the scrim has no background, and that is asserted rather than tolerated.
    /// </summary>
    /// <remarks>
    /// <para><b>Asserting null, not asserting a colour.</b> The renderer returns null here on purpose:
    /// the theme ships with the assembly, so a missing brush means the host did not include it, and
    /// painting a made-up grey would look deliberate. If somebody later hardcodes a fallback, this goes
    /// red and they have to answer why the renderer started deciding what "no theme" looks like. That is
    /// a question worth being asked once; it is not worth answering silently.</para>
    ///
    /// <para>The card is still readable — the dialog draws its own text and its buttons keep the Fluent
    /// template — so the cost of the missing scrim is "no dimming", not "no dialog".</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_dialog_without_the_theme_has_no_scrim_colour()
    {
        var card = Unthemed(out _);

        ButtonByLabel(card, "End").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var scrim = Overlay(card);

        Assert.NotNull(scrim);
        Assert.Null(scrim!.Background);
    }

    /// <summary>
    /// A host that defines its own copy of a word wins over the one the theme ships.
    /// </summary>
    /// <remarks>
    /// <para><b>The fallback test above does not prove this.</b> <c>Message()</c> reads through
    /// <c>TryFindResource</c>, which finds the theme's English whether or not the host overrode it — the
    /// two situations produce the same string, so an assertion that the English appears says nothing
    /// about the override. They are separate claims and need separate tests.</para>
    ///
    /// <para>A resource on the window is the same mechanism a host uses with a dictionary on its
    /// application: <c>DynamicResource</c> walks outward from the control and the nearest definition
    /// wins. Putting it on the window keeps the test inside the fixture instead of mutating the shared
    /// <see cref="Application"/> for every other test in the run.</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_host_that_defines_its_own_words_is_read_instead_of_the_themes()
    {
        var card = Unthemed(out var window);

        window.Resources["KitXDangerTitle"] = "要结束这一批吗？";
        window.Resources["KitXDangerConfirm"] = "结束";
        window.Resources["KitXDangerCancel"] = "保留";

        ButtonByLabel(card, "End").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var overlay = Overlay(card);

        Assert.NotNull(overlay);

        var words = overlay!.GetVisualDescendants()
            .OfType<Control>()
            .Select(control => control switch
            {
                TextBlock block => block.Text,
                Button button => button.Content as string,
                _ => null,
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        Assert.Contains("要结束这一批吗？", words);
        Assert.Contains("结束", words);
        Assert.Contains("保留", words);

        // The English must be gone, not merely joined by the override: a dialog showing both is the
        // failure mode of appending to a list instead of replacing a lookup.
        Assert.DoesNotContain("Are you sure?", words);
    }

    /// <summary>
    /// The card itself carries no style of its own, so a host's background shows through.
    /// </summary>
    /// <remarks>
    /// This is the other half of "no theme": the card must not paint a background it was never given.
    /// If it did, a host's dark surface would be covered by a light rectangle and the only symptom would
    /// be a card that looks wrong on one theme.
    /// </remarks>
    [AvaloniaFact]
    public void An_unthemed_card_does_not_paint_over_the_host()
    {
        var card = Unthemed(out _);

        Assert.Null(card.Background);
    }

    /// <summary>Guards the assertion above from passing because the type never had a background.</summary>
    /// <remarks>
    /// The first version of this asked <c>typeof(Control).GetProperty("Background")</c> and got null —
    /// <c>Background</c> is declared on <c>Panel</c>, not on <c>Control</c>. The card is a <c>Grid</c>,
    /// and <c>Grid</c> is a <c>Panel</c>, so the property genuinely exists and genuinely reads null;
    /// checking the wrong base type would have turned "nobody set it" into "it could not have one".
    /// </remarks>
    [AvaloniaFact]
    public void The_card_is_a_panel_that_could_have_a_background()
    {
        var card = Unthemed(out _);

        Assert.True(typeof(Grid).IsSubclassOf(typeof(Panel)));
        Assert.NotNull(typeof(Panel).GetProperty(nameof(Panel.Background)));
        Assert.Null(card.Background);
    }
}
