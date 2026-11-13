using System.Text.Json;
using KitX.ToolKit.Contract.View;
using Avalonia.Controls;
using global::Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
        var card = Card(Doc(new SurfaceTextElement { Row = 1, Key = "title" }));

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
            new SurfaceTextElement { Row = 1, Key = "cleared" },
            new SurfaceTextElement { Row = 2, Key = "blank" });

        var card = Card(document);

        // No value ever set: the cell is empty and still one slot tall.
        Assert.Equal(string.Empty, card.ShownTextOf("cleared"));
        Assert.Equal(SurfaceMetrics.FieldHeight, Cells(card)[0].Bounds.Height, 0.5);

        // Then a value, then removal — back to empty, still one slot.
        card.SetValue("cleared", Json("\"something\""));
        Assert.Equal("something", card.ShownTextOf("cleared"));

        card.RemoveValue("cleared");
        Assert.Equal(string.Empty, card.ShownTextOf("cleared"));
        Assert.Equal(SurfaceMetrics.FieldHeight, Cells(card)[0].Bounds.Height, 0.5);

        // An empty string is a value, and it shows as empty too — but it arrived as a value, which
        // is what the assertion below the empty check is really pinning.
        card.SetValue("blank", Json("\"\""));
        Assert.Equal(string.Empty, card.ShownTextOf("blank"));
        Assert.Equal(SurfaceMetrics.FieldHeight, Cells(card)[0].Bounds.Height, 0.5);
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
        var first = Doc(new SurfaceTextElement { Row = 1, Key = "title" });
        var card = Card(first);

        card.SetValue("title", Json("\"X\""));
        Assert.Equal("X", card.ShownTextOf("title"));

        // A second version of the document: same key, moved one row down.
        var second = Doc(
            new SurfaceTextElement { Row = 1, Key = "padding" },
            new SurfaceTextElement { Row = 2, Key = "title" });

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
        var card = Card(Doc(new SurfaceInputElement { Row = 1, Name = "barcode" }));
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
        var card = Card(Doc(new SurfaceListElement { Row = 1, Key = "log" }));

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
            new SurfaceTextElement { Row = 1, Key = "title" },
            new SurfaceTextElement { Row = 2, Key = "subtitle" }));

        card.SetValue("title", Json("\"A\""));
        card.SetValue("subtitle", Json("\"B\""));

        card.ClearAll();

        Assert.Equal(string.Empty, card.ShownTextOf("title"));
        Assert.Equal(string.Empty, card.ShownTextOf("subtitle"));

        // The cells themselves are still there, one slot tall each.
        Assert.Equal(SurfaceMetrics.FieldHeight, Cells(card)[0].Bounds.Height, 0.5);
        Assert.Equal(2, CardProbe.Elements(card).Count);
    }

    [AvaloniaFact]
    public void A_switch_is_drawn_as_a_tick_box_and_ignores_stored_values()
    {
        // CheckBox derives from Button, so an arm ordering that put Button first would treat every
        // tick box as a label and this assertion would read "False" forever.
        var card = Card(Doc(new SurfaceSwitchElement { Row = 1, Name = "enabled" }));

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

    /// <summary>A Switch that names a key does take that key's value, and shows it as a tick.</summary>
    /// <remarks>
    /// <para><b>The test above named the danger and then stepped around it.</b> Its comment says an
    /// arm ordering that put <c>Button</c> first would treat every tick box as a label — and then it
    /// gives the Switch no <c>key</c>, so <c>SetValue</c> never reaches <c>Show</c> at all and the
    /// ordering cannot be observed. The comment was right about the hazard and the fixture never put
    /// anything in front of it.</para>
    ///
    /// <para><b>What the defect looked like.</b> <c>CheckBox : ToggleButton : Button</c>, so the
    /// <c>case Button</c> arm matched a Switch's cell first and wrote the value into <c>Content</c>:
    /// a Switch whose key held <c>true</c> drew an unticked box with the word "true" beside it.
    /// Measured before the fix — <c>IsChecked=False Content=true</c>.</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_switch_with_a_key_shows_that_keys_value_as_a_tick_box()
    {
        var card = Card(Doc(new SurfaceSwitchElement { Row = 1, Key = "armed" }));

        var tick = CellOfType<CheckBox>(card);

        card.SetValue("armed", Json("true"));
        Relayout();

        Assert.Equal(true, tick.IsChecked);

        // Not into Content: a label beside a tick box is the shape this defect took, and a Switch
        // reading "true" is a Switch nobody can tell is on.
        Assert.Null(tick.Content);

        card.SetValue("armed", Json("false"));
        Relayout();
        Assert.Equal(false, tick.IsChecked);

        // A value that is not a bool cannot be expressed by a tick box. Unticked, not "whatever it
        // was" — leaving the previous state would show a tick for a value that is not true.
        card.SetValue("armed", Json("\"maybe\""));
        Relayout();
        Assert.Equal(false, tick.IsChecked);
        Assert.Null(tick.Content);
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
            new SurfaceSelectElement { Row = 1, Name = "mode", Options = ["fast", "safe"] },
            new SurfaceButtonElement { Row = 2, Label = "Submit", Events = ["Submit"] }));

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
            new SurfaceSelectElement { Row = 1, Name = "mode", OptionsKey = "choices" },
            new SurfaceButtonElement { Row = 2, Label = "Submit", Events = ["Submit"] }));

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
    /// <remarks>
    /// Through <see cref="CardProbe"/> because since Q101 the card's children are rows, not elements —
    /// reading them directly would count rows and call them cells.
    /// </remarks>
    private static IReadOnlyList<Control> Cells(SurfaceCard card) => CardProbe.Elements(card);

    /// <summary>The one cell of the given type on the card.</summary>
    /// <remarks>
    /// By type, not by key. Since Q71 an editing element may not carry a key at all, and its name is
    /// an upload identity rather than a handle for the test to hold — reaching into the visual tree by
    /// name would make these assertions about what the element is called instead of what it does.
    /// </remarks>
    private static T CellOfType<T>(SurfaceCard card) where T : Control =>
        card.GetVisualDescendants().OfType<T>().Single();

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

    /// <summary>
    /// An appended entry reaches a list that was built from a document, by the route the wire uses.
    /// </summary>
    /// <remarks>
    /// <para><b>Every other append test in this repository hands the card a control it never
    /// builds.</b> <c>Appending_keeps_the_reader_where_they_were...</c> reads the <c>ScrollViewer</c>
    /// out of the visual tree and appends after a <c>SetValue</c>, which works — but the append path
    /// itself looks a cell up in the card's own table, and <b>since Q99 a List's entry in that table
    /// is its group container (<c>Border</c>), not the scroller</b>. The old code required the table
    /// entry to be a <c>ScrollViewer</c> and returned silently when it was not, so append did
    /// nothing at all in the real product while a test that had the scroller in hand still passed.</para>
    ///
    /// <para><b>What makes this one different is only the order.</b> Append first, with no
    /// <c>SetValue</c> before it, so the card has to find the cell the same way the message does.
    /// That is the whole defect: it lived in the lookup, not in the insert.</para>
    /// </remarks>
    [AvaloniaFact]
    public void An_appended_entry_reaches_a_list_the_card_built_itself()
    {
        var card = Card(Doc(new SurfaceListElement { Row = 1, Key = "log", ItemTitle = "at" }));

        // No SetValue first. The wire sends `appended` deltas for a stream key from the very first
        // entry, so the card must be able to append into a list it has never had a value for.
        card.AppendValue("log", Json("\"scanned\""));
        Relayout();

        Assert.Equal("scanned", card.ShownTextOf("log"));

        // Two entries, because one is the count at which a silent no-op and a working append look
        // identical when the cell is read through its content rather than through its children.
        card.AppendValue("log", Json("\"scanned again\""));
        Relayout();

        Assert.Equal("scanned\nscanned again", card.ShownTextOf("log"));
    }

    /// <summary>
    /// After a document change, a key that feeds a Select reaches the Select the card just built.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the second instance of the same asymmetry, found by asking the same structural
    /// question.</b> <see cref="SurfaceCard.SetDocument"/> clears <c>_cells</c>, <c>_names</c> and
    /// <c>_selection</c> — every map keyed by an element of the document — and leaves
    /// <c>_optionSources</c> alone. That map holds <c>ComboBox</c> instances, which the rebuild has
    /// just removed from the tree.</para>
    ///
    /// <para><b>The failure is silent and shaped like the append defect.</b>
    /// <see cref="SurfaceCard.SetValue"/> asks <c>ReplaceCandidates</c> first and returns early when
    /// that key feeds any Select. With a stale entry present, the key feeds only combos that are no
    /// longer on the card: the early return fires, the new combo never receives candidates, and the
    /// value never reaches the screen. No exception, no warning — the drop-down stays empty.</para>
    /// </remarks>
    /// <summary>
    /// After a document change, a key that stopped feeding a Select can still reach a Text cell.
    /// </summary>
    /// <remarks>
    /// <para><b>This is where the stale <c>_optionSources</c> entry becomes visible.</b>
    /// <see cref="SurfaceCard.SetValue"/> asks <c>ReplaceCandidates</c> first and returns early when
    /// that key feeds <i>any</i> Select. The map is never cleared on a rebuild, so a key that fed a
    /// Select in the previous document still "feeds" the combos that were removed from the tree —
    /// and the early return fires, so the Text cell that now owns the key never shows anything.</para>
    ///
    /// <para><b>What the reader sees is a blank cell, with no error anywhere.</b> The value is in the
    /// projection, the document declares the cell, the cell is on screen, and it stays empty. That is
    /// the same invisible shape as the append defect: a correct-looking early return that has gone
    /// stale.</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_key_that_stopped_feeding_a_select_still_reaches_its_text_cell()
    {
        var card = Card(Doc(new SurfaceSelectElement
        {
            Row = 1,
            Name = "session",
            OptionsKey = "sessions",
        }));

        card.SetValue("sessions", Json("[\"one\"]"));
        Relayout();

        // The same declared key, now shown as text rather than feeding a picker.
        card.SetDocument(Doc(new SurfaceTextElement { Row = 1, Key = "sessions" }));
        Relayout();

        card.SetValue("sessions", Json("\"two\""));
        Relayout();

        Assert.Equal("two", card.ShownTextOf("sessions"));
    }

    /// <summary>
    /// After a document change, a key that stopped feeding a Select can still be cleared.
    /// </summary>
    /// <remarks>
    /// <see cref="SurfaceCard.RemoveValue"/> has the same early return as <c>SetValue</c>, one line
    /// further down and for the same reason: "this key feeds a picker, so clearing it means emptying
    /// the picker". Once the picker is gone, that branch clears a control nobody can see and returns
    /// before the Text cell is touched — so a removal leaves the old value on screen.
    /// </remarks>
    [AvaloniaFact]
    public void A_key_that_stopped_feeding_a_select_can_still_be_cleared()
    {
        var card = Card(Doc(new SurfaceSelectElement
        {
            Row = 1,
            Name = "session",
            OptionsKey = "sessions",
        }));

        card.SetValue("sessions", Json("[\"one\"]"));
        Relayout();

        card.SetDocument(Doc(new SurfaceTextElement { Row = 1, Key = "sessions" }));
        Relayout();

        card.SetValue("sessions", Json("\"two\""));
        Relayout();
        Assert.Equal("two", card.ShownTextOf("sessions"));

        card.RemoveValue("sessions");
        Relayout();

        Assert.Equal(string.Empty, card.ShownTextOf("sessions"));
    }
}
