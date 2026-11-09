using System.Text.Json;
using KitX.ToolKit.Contract.View;
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
        Assert.Equal(SurfaceMetrics.SlotHeight, Cells(card)[0].Bounds.Height, 0.5);

        // Then a value, then removal — back to empty, still one slot.
        card.SetValue("cleared", Json("\"something\""));
        Assert.Equal("something", card.ShownTextOf("cleared"));

        card.RemoveValue("cleared");
        Assert.Equal(string.Empty, card.ShownTextOf("cleared"));
        Assert.Equal(SurfaceMetrics.SlotHeight, Cells(card)[0].Bounds.Height, 0.5);

        // An empty string is a value, and it shows as empty too — but it arrived as a value, which
        // is what the assertion below the empty check is really pinning.
        card.SetValue("blank", Json("\"\""));
        Assert.Equal(string.Empty, card.ShownTextOf("blank"));
        Assert.Equal(SurfaceMetrics.SlotHeight, Cells(card)[0].Bounds.Height, 0.5);
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
    public void An_editing_control_is_never_written_to_from_a_value()
    {
        // Since Q71 an editing element carries no key at all — the validator strips one if the author
        // wrote it. So there is no path from a stored value into a TextBox, and the value the person
        // typed is the only one the control will ever hold. SetValue is called here with the element's
        // *name*, which is an upload identity rather than a data key, to show that the two are not
        // interchangeable: a name addresses an event payload, never a cell.
        var card = Card(Doc(new SurfaceInputElement { Row = 1, Column = 1, Name = "barcode" }));
        var box = CellOfType<TextBox>(card);

        box.Text = "scanned";
        card.SetValue("barcode", Json("\"from-the-server\""));
        Relayout();

        Assert.Equal("scanned", box.Text);
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

        var scroller = CellOfType<ScrollViewer>(card);
        scroller.Offset = new Vector(0, 100);
        var afterScroll = scroller.Offset.Y;
        Assert.True(afterScroll > 0, "the list has to be scrolled for this assertion to mean anything");

        card.AppendValue("log", Json("\"line 40\""));

        // Same control instance throughout: appending did not rebuild it.
        Assert.Same(scroller, CellOfType<ScrollViewer>(card));
        Assert.Equal(afterScroll, scroller.Offset.Y, 0.5);

        // Replacing does rebuild the content, and the offset goes with it.
        card.SetValue("log", Json($"[{many}]"));
        Relayout();
        Assert.Equal(0, CellOfType<ScrollViewer>(card).Offset.Y, 0.5);
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
        Assert.Equal(SurfaceMetrics.SlotHeight, Cells(card)[0].Bounds.Height, 0.5);
        Assert.Equal(2, card.Children.OfType<Control>().Count());
    }

    [AvaloniaFact]
    public void A_switch_is_drawn_as_a_tick_box_and_ignores_stored_values()
    {
        // CheckBox derives from Button, so an arm ordering that put Button first would treat every
        // tick box as a label and this assertion would read "False" forever.
        var card = Card(Doc(new SurfaceSwitchElement { Row = 1, Column = 1, Name = "enabled" }));

        var tick = CellOfType<CheckBox>(card);

        // A stored value does not reach an editing control — it carries no key to arrive by. What
        // the control shows is the person's own input, which is the whole reason the payload has to
        // come from the client rather than from the key table.
        Assert.NotEqual(true, tick.IsChecked);

        card.SetValue("enabled", Json("true"));
        Relayout();
        Assert.NotEqual(true, tick.IsChecked);

        tick.IsChecked = true;
        Assert.Equal(true, tick.IsChecked);
    }

    /// <summary>A row the person picked travels with the next event.</summary>
    /// <remarks>
    /// <para><b>This test had no reason to exist until the wiring was fixed, because the wiring was
    /// missing.</b> <c>SelectionChanged</c> was never subscribed: <c>_selection</c> was written only
    /// when a rebuild carried a previous pick forward, so a freshly built card uploaded nothing for a
    /// <c>Select</c> no matter what was chosen on it. Every other editing kind reads its control at
    /// upload time and so got away with it; a <c>ComboBox</c> cannot, because it shows a placeholder
    /// whenever its list does not contain the value.</para>
    ///
    /// <para>The failure this pins down is silent in production: the card looks right, the person's
    /// choice is visible, and the event payload simply has no entry for it.</para>
    /// </remarks>
    [AvaloniaFact]
    public void The_row_a_person_picks_travels_with_the_next_event()
    {
        var card = Card(Doc(
            new SurfaceSelectElement { Row = 1, Column = 1, Name = "mode", Options = ["fast", "safe"] },
            new SurfaceButtonElement { Row = 2, Column = 1, Label = "Submit", Events = ["Submit"] }));

        var picker = CellOfType<ComboBox>(card);
        Assert.Null(picker.SelectedItem);

        picker.SelectedItem = "safe";
        Relayout();

        SurfaceEvent? sent = null;
        card.SomethingHappened += (_, e) => sent = e;
        Press(CellOfType<Button>(card));

        Assert.Equal("event:Submit", sent?.Kind);
        Assert.Equal("safe", sent?.Values["mode"].GetString());
    }

    /// <summary>A pick that leaves the candidate list is still what the card holds and uploads.</summary>
    /// <remarks>
    /// The list changing under a pick is the normal case, not a corner: a session list refreshes and
    /// the row the person chose is gone from it. The renderer keeps the value rather than deciding it
    /// stopped being valid — that judgement belongs to whoever owns the list.
    /// </remarks>
    [AvaloniaFact]
    public void A_pick_that_the_candidates_no_longer_offer_is_kept_rather_than_cleared()
    {
        var card = Card(Doc(
            new SurfaceSelectElement { Row = 1, Column = 1, Name = "mode", OptionsKey = "choices" },
            new SurfaceButtonElement { Row = 2, Column = 1, Label = "Submit", Events = ["Submit"] }));

        var picker = CellOfType<ComboBox>(card);

        card.SetValue("choices", Json("[\"one\",\"two\"]"));
        Relayout();
        picker.SelectedItem = "two";
        Relayout();

        card.SetValue("choices", Json("[\"three\"]"));
        Relayout();

        // The control cannot hold what its list does not contain, so it shows a placeholder — but
        // the value itself is still there, and still uploads.
        Assert.Null(picker.SelectedItem);
        Assert.Equal(["three"], card.CandidatesOf("choices"));

        SurfaceEvent? sent = null;
        card.SomethingHappened += (_, e) => sent = e;
        Press(CellOfType<Button>(card));

        Assert.Equal("two", sent?.Values["mode"].GetString());
    }

    /// <summary>Presses a button the way the input system would, minus the pointer.</summary>
    /// <remarks>
    /// A headless session runs no pointer input, so a click has to be delivered as the routed event
    /// the pointer would have raised. This is the same handler the real click reaches — not a second
    /// path into <c>Raise</c>.
    /// </remarks>
    private static void Press(Button button) =>
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>Every control the card added, in the card's own order.</summary>
    private static List<Control> Cells(SurfaceCard card) =>
        [.. card.Children.OfType<Control>().OrderBy(c => c.Bounds.Top)];

    /// <summary>The one cell of the given type on the card.</summary>
    /// <remarks>
    /// By type, not by key. Since Q71 an editing element may not carry a key at all, and its name is
    /// an upload identity rather than a handle for the test to hold — reaching into the visual tree by
    /// name would make these assertions about what the element is called instead of what it does.
    /// </remarks>
    private static T CellOfType<T>(SurfaceCard card) where T : Control =>
        card.Children.OfType<T>().Single();

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
