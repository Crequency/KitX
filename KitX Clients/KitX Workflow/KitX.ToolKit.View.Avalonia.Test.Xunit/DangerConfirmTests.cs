using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using global::Avalonia.VisualTree;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;
using System.Text.Json;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// A <c>danger</c> button asks first, and a <c>normal</c> one does not.
/// </summary>
/// <remarks>
/// <para><b>The interesting assertion is the one about what was never sent.</b> A confirmation that
/// raised the event and then decided not to forward it would pass every test that counts the events
/// arriving at the server; the difference only shows up if the test can see the moment before. These
/// tests subscribe to <see cref="SurfaceCard.SomethingHappened"/> directly, which is upstream of
/// every consumer, so "nothing was raised" is observable rather than inferred.</para>
///
/// <para><b>The dialog is a <c>Panel</c> inside the card, which is why this is testable at all.</b>
/// A separate window would need a modal loop and an owner; an overlay is a child of the control under
/// test, so its buttons are clicked the same way every other button on this card is.</para>
/// </remarks>
public sealed class DangerConfirmTests
{
    private const string Document = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Text",   "key": "title", "row": 1, "weight": 1 },
            { "type": "Input",  "name": "barcode", "row": 2, "weight": 1 },
            { "type": "Button", "label": "Save", "events": ["Save"], "row": 3, "weight": 1 },
            { "type": "Button", "label": "End the batch", "events": ["End"], "emphasis": "danger", "row": 4, "weight": 1 }
          ]
        }
        """;

    private static SurfaceCard Make(out List<SurfaceEvent> sent)
    {
        var card = new SurfaceCard();
        card.SetDocument(JsonSerializer.Deserialize<SurfaceDocument>(Document)!);

        var window = Host;
        window.Content = card;
        Layout(window, card);

        // A local rather than an out parameter: a lambda cannot capture one, and this list has to be
        // captured by the subscription below.
        var collected = new List<SurfaceEvent>();
        sent = collected;
        card.SomethingHappened += (_, e) => collected.Add(e);

        return card;
    }

    private static void Layout(Window window, SurfaceCard card)
    {
        window.Content = card;
        window.Measure(new Size(card.Width, card.Height));
        window.Arrange(new Rect(0, 0, card.Width, card.Height));
        window.UpdateLayout();
    }

    private static Window Host
    {
        get
        {
            if (_host is not null)
            {
                _ = _host.Content;
                return _host;
            }

            _host = new Window { Width = SurfaceMetrics.CardWidth, Height = SurfaceMetrics.CardHeight };
            _host.Show();
            return _host;
        }
    }

    private static Window? _host;

    /// <summary>The card's own controls, not the ones a template put there.</summary>
    private static IReadOnlyList<Control> Elements(SurfaceCard card) => CardProbe.Elements(card);

    private static Button ButtonByLabel(SurfaceCard card, string label) =>
        Elements(card).OfType<Button>().Single(b => (string?)b.Content == label);

    /// <summary>The confirmation overlay, or null when none is open.</summary>
    /// <remarks>
    /// <b>Filtered by class, and that is not decoration.</b> A row is a <c>Grid</c>, and <c>Grid</c>
    /// derives from <c>Panel</c>, so <c>card.Children.OfType&lt;Panel&gt;()</c> is every row plus the
    /// overlay. The first version of the stacking test asked for panels and got five.
    /// </remarks>
    private static Panel? Overlay(SurfaceCard card) =>
        card.Children.OfType<Panel>().SingleOrDefault(p => p.Classes.Contains(SurfaceCard.ThemedScrim));

    private static Button DialogButton(SurfaceCard card, string label) =>
        Overlay(card)!.GetVisualDescendants()
            .OfType<Button>()
            .Single(b => (string?)b.Content == label);

    [AvaloniaFact]
    public void A_danger_click_asks_before_it_raises_anything()
    {
        var card = Make(out var sent);

        ButtonByLabel(card, "End the batch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Empty(sent);
        Assert.NotNull(Overlay(card));
    }

    [AvaloniaFact]
    public void Cancelling_closes_the_dialog_and_sends_nothing()
    {
        var card = Make(out var sent);

        ButtonByLabel(card, "End the batch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        DialogButton(card, "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Empty(sent);
        Assert.Null(Overlay(card));
    }

    [AvaloniaFact]
    public void Confirming_sends_the_events_and_closes_the_dialog()
    {
        var card = Make(out var sent);

        ButtonByLabel(card, "End the batch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        DialogButton(card, "Continue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Single(sent);
        Assert.Equal("event:End", sent[0].Kind);
        Assert.Null(Overlay(card));
    }

    [AvaloniaFact]
    public void A_button_that_is_not_danger_sends_at_once_and_opens_nothing()
    {
        var card = Make(out var sent);

        ButtonByLabel(card, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Single(sent);
        Assert.Equal("event:Save", sent[0].Kind);
        Assert.Null(Overlay(card));
    }

    [AvaloniaFact]
    public void A_second_danger_click_does_not_stack_a_second_dialog()
    {
        var card = Make(out _);

        ButtonByLabel(card, "End the batch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // The second click is behind the scrim by then, so it is raised directly rather than clicked:
        // the point is what ShowConfirm does with a request that arrives while one is already open.
        ButtonByLabel(card, "End the batch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // Filtered by class, and named, because a row is a Panel too: counting Panels would count the
        // rows. The name is what carries that, not the predicate — Assert.Single's overload would have
        // hidden the filter inside the call, where the next reader cannot see what is being counted.
        var scrims = card.Children.Where(c => c.Classes.Contains(SurfaceCard.ThemedScrim)).ToList();
        Assert.Single(scrims);
    }

    [AvaloniaFact]
    public void A_document_change_closes_an_open_confirmation()
    {
        var card = Make(out var sent);

        ButtonByLabel(card, "End the batch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.NotNull(Overlay(card));

        // The button the person was being asked about is gone from the new document. Confirming an
        // action that no longer exists is worse than dropping the click: they would believe they
        // approved something and nothing would be sent.
        card.SetDocument(JsonSerializer.Deserialize<SurfaceDocument>(Document)!);

        Assert.Null(Overlay(card));
        Assert.Empty(sent);
    }

    [AvaloniaFact]
    public void The_overlay_is_not_a_row()
    {
        var card = Make(out _);

        var rowsBefore = CardProbe.Rows(card).Count;
        var childrenBefore = card.Children.Count;

        ButtonByLabel(card, "End the batch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // Two numbers rather than one, because they answer different questions. Rows stayed the same
        // because the overlay is not a row; children grew because it is on the card. A single count
        // would have made "the overlay opened" and "a row was added" the same observation.
        Assert.Equal(rowsBefore, CardProbe.Rows(card).Count);
        Assert.Equal(childrenBefore + 1, card.Children.Count);
    }
}
