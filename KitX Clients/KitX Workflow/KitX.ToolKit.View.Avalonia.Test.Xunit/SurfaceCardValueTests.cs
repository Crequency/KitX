using System.Text.Json;
using Avalonia.Controls;
using global::Avalonia;
using Avalonia.Headless.XUnit;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// A value reaches the screen, and stays there under the changes the protocol describes.
/// </summary>
/// <remarks>
/// <para><b>This file exists because every renderer test before it proved something narrower than it
/// looked.</b> Fourteen tests asserted slot counts, control sorts and positions, and not one of them
/// ever put a value in front of a reader — the card drew <c>text.Key</c>, not the value that key
/// held. Layout green, coverage green, and nothing had ever appeared on screen.</para>
///
/// <para><b>The assertion this file is built around is deliberately the weakest possible one and the
/// only one that could not be faked:</b> after <c>SetValue</c>, the text a reader would see is that
/// value. Every other property of the card can be right while that is wrong, and no amount of
/// checking layout will notice.</para>
/// </remarks>
public sealed class SurfaceCardValueTests
{
    private static JsonElement Json(string literal) => JsonDocument.Parse(literal).RootElement.Clone();

    /// <summary>A one-column document whose rows are the given elements, in order.</summary>
    private static SurfaceDocument Doc(params SurfaceElement[] elements) => new()
    {
        Elements = elements,
    };

    private static SurfaceCard Card(SurfaceDocument document)
    {
        var card = new SurfaceCard();
        card.SetDocument(document);

        var window = Host;
        window.Content = card;
        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();

        return card;
    }

    [AvaloniaFact]
    public void A_value_sent_to_a_text_element_appears_on_the_card()
    {
        // The one assertion this whole stage exists for.
        var card = Card(Doc(new SurfaceTextElement { Row = 1, Column = 1, Key = "title" }));

        card.SetValue("title", Json("\"KitX Dashboard\""));

        Assert.Equal("KitX Dashboard", card.ShownTextOf("title"));
    }

    [AvaloniaFact]
    public void A_value_and_no_value_both_show_nothing_but_keep_their_row()
    {
        // Two different premises, two assertions. §10.2.1.1's rule is that an element occupies its
        // slot whether or not it has a value; Q67's rule is that an empty string is a value, not an
        // absence. Folded into one assertion — "both look empty" — the second could be broken and
        // nobody would find out, because the first would still pass.
        var document = Doc(
            new SurfaceTextElement { Row = 1, Column = 1, Key = "cleared" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "blank" });

        var card = Card(document);

        // No value ever set: the cell is empty and still one slot tall.
        Assert.Equal(string.Empty, card.ShownTextOf("cleared"));
        Assert.Equal(SurfaceMetrics.SlotHeight, ChildOf(card, "cleared").Bounds.Height, 0.5);

        // Then a value, then removal — back to empty, still one slot.
        card.SetValue("cleared", Json("\"something\""));
        Assert.Equal("something", card.ShownTextOf("cleared"));

        card.RemoveValue("cleared");
        Assert.Equal(string.Empty, card.ShownTextOf("cleared"));
        Assert.Equal(SurfaceMetrics.SlotHeight, ChildOf(card, "cleared").Bounds.Height, 0.5);

        // An empty string is a value, and it shows as empty too — but it arrived as a value, which
        // is what the assertion below the empty check is really pinning.
        card.SetValue("blank", Json("\"\""));
        Assert.Equal(string.Empty, card.ShownTextOf("blank"));
        Assert.Equal(SurfaceMetrics.SlotHeight, ChildOf(card, "blank").Bounds.Height, 0.5);
    }

    [AvaloniaFact]
    public void After_a_document_change_a_keyed_cell_shows_only_what_was_sent_next()
    {
        // The criterion that keeps a second source of truth out of this class.
        //
        // SetDocument rebuilds the cells and carries nothing that came from a key. So a caller that
        // wants values after a surface push must send them again — and the last assertion here is
        // the one that matters: after a rebuild with no SetValue, the bound cell is EMPTY. An
        // implementation that cached values internally would pass the first three assertions and
        // fail the last, which is exactly the point of writing it.
        var first = Doc(new SurfaceTextElement { Row = 1, Column = 1, Key = "title" });
        var card = Card(first);

        card.SetValue("title", Json("\"X\""));
        Assert.Equal("X", card.ShownTextOf("title"));

        // A second version of the document: same key, moved one row down.
        var second = Doc(
            new SurfaceTextElement { Row = 1, Column = 1, Key = "padding" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "title" });

        card.SetDocument(second);

        // Rebuilt: nothing that came from a key survived.
        Assert.Equal(string.Empty, card.ShownTextOf("title"));

        // And the caller's next message is what shows.
        card.SetValue("title", Json("\"Y\""));
        Assert.Equal("Y", card.ShownTextOf("title"));
    }

    [AvaloniaFact]
    public void An_editing_controls_half_typed_input_survives_a_document_change()
    {
        // The one exception to "nothing is carried", and it is not a contradiction: Q88 says an
        // Input's contents exist only inside the control, so nobody can resend what the user typed.
        // Every other kind has an owner who can.
        var first = Doc(new SurfaceInputElement { Row = 1, Column = 1, Key = "barcode" });
        var card = Card(first);

        var box = Assert.IsType<TextBox>(ChildOf(card, "barcode"));
        box.Text = "half-typed";

        // Same document, one row added above it — enough to force a full rebuild.
        var second = Doc(
            new SurfaceTextElement { Row = 1, Column = 1, Key = "heading" },
            new SurfaceInputElement { Row = 2, Column = 1, Key = "barcode" });

        card.SetDocument(second);

        var rebuilt = Assert.IsType<TextBox>(ChildOf(card, "barcode"));
        Assert.Equal("half-typed", rebuilt.Text);
    }

    [AvaloniaFact]
    public void Appending_keeps_the_reader_where_they_were_and_replacing_does_not()
    {
        // Append and replace are different messages, so they are different paths. Append never swaps
        // the scrolling content, so the position survives without anyone remembering it; replace
        // puts the reader back at the top because the content is not the same content.
        var card = Card(Doc(new SurfaceListElement { Row = 1, Column = 1, Key = "log" }));

        var many = string.Join(",", Enumerable.Range(0, 40).Select(i => $"\"line {i}\""));
        card.SetValue("log", Json($"[{many}]"));

        // Adding rows invalidates measure, and nothing in a headless session pumps a layout pass.
        // Without this the extent is still the pre-value one and the scroll assertion below reads a
        // list that holds forty entries yet claims to need 64 pixels.
        Relayout();

        var scroller = Assert.IsType<ScrollViewer>(ChildOf(card, "log"));
        scroller.Offset = new Vector(0, 100);
        var afterScroll = scroller.Offset.Y;
        Assert.True(afterScroll > 0, "the list has to be scrolled for this assertion to mean anything");

        card.AppendValue("log", Json("\"line 40\""));

        // Same control instance throughout: appending did not rebuild it.
        Assert.Same(scroller, ChildOf(card, "log"));
        Assert.Equal(afterScroll, scroller.Offset.Y, 0.5);

        // Replacing does rebuild the content, and the offset goes with it.
        card.SetValue("log", Json($"[{many}]"));
        Relayout();
        Assert.Equal(0, Assert.IsType<ScrollViewer>(ChildOf(card, "log")).Offset.Y, 0.5);
    }

    [AvaloniaFact]
    public void Clearing_every_key_empties_the_cells_and_keeps_them()
    {
        // cleared means the toolkit's keys are gone, not that the card has no layout.
        var card = Card(Doc(
            new SurfaceTextElement { Row = 1, Column = 1, Key = "title" },
            new SurfaceTextElement { Row = 2, Column = 1, Key = "subtitle" }));

        card.SetValue("title", Json("\"A\""));
        card.SetValue("subtitle", Json("\"B\""));

        card.ClearAll();

        Assert.Equal(string.Empty, card.ShownTextOf("title"));
        Assert.Equal(string.Empty, card.ShownTextOf("subtitle"));

        // The cells themselves are still there, one slot tall each.
        Assert.Equal(SurfaceMetrics.SlotHeight, ChildOf(card, "title").Bounds.Height, 0.5);
        Assert.Equal(2, card.Children.OfType<Control>().Count());
    }

    [AvaloniaFact]
    public void A_switch_shows_whether_its_value_is_true()
    {
        // CheckBox derives from Button, so an arm ordering that put Button first would treat every
        // tick box as a label and this assertion would read "False" forever.
        var card = Card(Doc(new SurfaceSwitchElement { Row = 1, Column = 1, Key = "enabled" }));

        var tick = Assert.IsType<CheckBox>(ChildOf(card, "enabled"));

        card.SetValue("enabled", Json("true"));
        Assert.True(tick.IsChecked);

        card.SetValue("enabled", Json("false"));
        Assert.False(tick.IsChecked);
    }

    /// <summary>One laid-out cell, by key.</summary>
    private static Control ChildOf(SurfaceCard card, string key) =>
        card.Children.OfType<Control>().Single(c => c.Name == key);

    /// <summary>Runs the layout pass a live window would otherwise run for us.</summary>
    /// <remarks>
    /// Every mutation through the five methods invalidates measure; nothing in a headless session
    /// pumps a pass afterwards. Calling this between a value change and a geometry assertion is what
    /// a running application does implicitly, so it is not a workaround — it is the missing pump.
    /// </remarks>
    private static void Relayout()
    {
        var window = Host;

        window.Measure(new Size(SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.Arrange(new Rect(0, 0, SurfaceMetrics.CardWidth, SurfaceMetrics.CardHeight));
        window.UpdateLayout();
    }

    /// <summary>The window the card is laid out inside.</summary>
    /// <remarks>
    /// One window, shown once, for the reason the layout tests share one: a templated control has no
    /// template until the visual tree reaches a root, and a root comes from a shown window. The
    /// editing kinds and the list are all templated.
    /// </remarks>
    private static Window Host
    {
        get
        {
            if (_host is not null)
                return _host;

            _host = new Window
            {
                Width = SurfaceMetrics.CardWidth,
                Height = SurfaceMetrics.CardHeight,
            };

            _host.Show();

            return _host;
        }
    }

    private static Window? _host;
}
