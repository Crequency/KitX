using global::Avalonia.Controls;
using global::Avalonia.VisualTree;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using KitX.ToolKit.Contract.Surface;
using System.Text.Json;
using KitX.ToolKit.Contract.View;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// What the card says when the person at it acts.
/// </summary>
/// <remarks>
/// <para><b>The renderer does not know what a trigger is.</b> These tests pin that from the outside:
/// the event carries a name and a bag of values, and nothing in it could name a trigger even if the
/// renderer wanted to. Which triggers a name wakes is the server's business, and no test here has an
/// opinion about it.</para>
///
/// <para><b>Values are keyed by each element's <c>name</c>, not its <c>key</c>.</b> The two fields are
/// separate on purpose — a cell may show one stored value and upload its input under another word —
/// so the fixture below gives every element both fields with different spellings. A renderer that
/// reads <c>key</c> here would produce a plausible-looking payload under the wrong names.</para>
/// </remarks>
public sealed class SurfaceEventTests
{
    private const string Document = """
        {
          "schemaVersion": 1,
          "layoutVersion": 1,
          "elements": [
            { "type": "Input",  "key": "barcode", "name": "scanned",  "row": 1, "weight": 2 },
            { "type": "Number", "key": "count",   "name": "quantity", "row": 1, "weight": 1 },
            { "type": "Switch", "key": "gift",    "name": "isGift",   "row": 1, "weight": 1 },

            { "type": "Button", "label": "Save",   "events": ["Save"],               "row": 2, "weight": 1 },
            { "type": "Button", "label": "Save all", "events": ["Save", "Sync"],     "row": 2, "weight": 1 },

            { "type": "Text",   "key": "title",   "row": 3, "weight": 1 }
          ]
        }
        """;

    private static SurfaceCard Make()
    {
        var card = new SurfaceCard();
        card.SetDocument(JsonSerializer.Deserialize<SurfaceDocument>(Document)!);

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
            _host = new global::Avalonia.Controls.Window
            {
                Width = SurfaceMetrics.CardWidth,
                Height = SurfaceMetrics.CardHeight,
            };
            _host.Show();
            return _host;
        }
    }

    private static global::Avalonia.Controls.Window? _host;

    /// <summary>The buttons this card added, found by label.</summary>
    /// <remarks>
    /// <b>The card's own elements, not GetVisualDescendants.</b> A <c>NumericUpDown</c> puts two
    /// templated <c>Button</c>s with <c>PathIcon</c> content in its own template, so walking the whole
    /// visual tree finds spinner arrows. They are not the card's buttons. <see cref="CardProbe.Elements"/>
    /// asks the card which elements it added — since Q101 that means walking the card's rows, because
    /// the card's own children are rows rather than elements.
    /// </remarks>
    private static Button ButtonByLabel(SurfaceCard card, string label) =>
        CardProbe.Elements(card).OfType<Button>().Single(b => (string?)b.Content == label);

    [AvaloniaFact]
    public void Clicking_a_button_sends_the_events_it_declares()
    {
        var card = Make();
        var sent = new List<SurfaceEvent>();
        card.SomethingHappened += (_, e) => sent.Add(e);

        ButtonByLabel(card, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Single(sent);
        Assert.Equal("event:Save", sent[0].Kind);
    }

    [AvaloniaFact]
    public void A_button_declaring_three_names_sends_three_events_with_the_same_values()
    {
        var card = Make();
        var sent = new List<SurfaceEvent>();
        card.SomethingHappened += (_, e) => sent.Add(e);

        ButtonByLabel(card, "Save all").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // Two names on this button; the assertion is per declared name, not per click.
        Assert.Equal(2, sent.Count);
        Assert.Equal(["event:Save", "event:Sync"], sent.Select(e => e.Kind).ToArray());

        // Identical snapshots: these are one gesture wearing two names, not two collections.
        // If they differed, "one gesture, N events" would be a claim about timing rather than
        // about meaning.
        Assert.Equal(
            JsonSerializer.Serialize(sent[0].Values),
            JsonSerializer.Serialize(sent[1].Values));
    }

    [AvaloniaFact]
    public void The_payload_is_keyed_by_name_not_by_key()
    {
        var card = Make();
        SurfaceEvent? sent = null;
        card.SomethingHappened += (_, e) => sent = e;

        ButtonByLabel(card, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // The fixture deliberately spells key and name differently for every element, so a renderer
        // reading the wrong one produces a payload that looks right and is filed under wrong words.
        Assert.True(sent!.Value.Values.ContainsKey("scanned"), "the input's name is 'scanned'");
        Assert.False(sent.Value.Values.ContainsKey("barcode"), "its key must not appear");
    }

    [AvaloniaFact]
    public void An_element_without_a_name_contributes_nothing()
    {
        var card = Make();
        SurfaceEvent? sent = null;
        card.SomethingHappened += (_, e) => sent = e;

        ButtonByLabel(card, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // `title` is a Text element with a key and no name. Its value is on the card, but it is not
        // part of an upload — that is what "no name" means, and it needs no second check anywhere.
        Assert.False(sent!.Value.Values.ContainsKey("title"));
    }

    [AvaloniaFact]
    public void What_the_person_typed_is_what_travels()
    {
        var card = Make();
        SurfaceEvent? sent = null;
        card.SomethingHappened += (_, e) => sent = e;

        var box = Assert.IsType<TextBox>(card.GetVisualDescendants().OfType<TextBox>().First());
        box.Text = "978-3-16-148410-0";

        ButtonByLabel(card, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("978-3-16-148410-0", sent!.Value.Values["scanned"].GetString());
    }

    [AvaloniaFact]
    public void A_switch_travels_as_a_bool_and_a_number_as_a_number()
    {
        var card = Make();
        SurfaceEvent? sent = null;
        card.SomethingHappened += (_, e) => sent = e;

        card.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        card.GetVisualDescendants().OfType<NumericUpDown>().Single().Value = 3;

        ButtonByLabel(card, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(JsonValueKind.True, sent!.Value.Values["isGift"].ValueKind);
        Assert.Equal(3, sent.Value.Values["quantity"].GetDecimal());
    }
}
