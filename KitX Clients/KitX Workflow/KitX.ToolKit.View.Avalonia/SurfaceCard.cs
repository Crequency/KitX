using Avalonia;
using Avalonia.Controls;
using System.Text.Json;

namespace KitX.ToolKit.View.Avalonia;

/// <summary>
/// A 390×845 card that renders a surface, and keeps it current as values change.
/// </summary>
/// <remarks>
/// <para><b>This class does not call <c>SurfaceLayout</c>.</b> That is deliberate and it is the
/// whole reason the renderer's completion criterion can mean anything. The criterion compares the
/// visual tree's Top differences against what <c>SurfaceLayout.PixelTopOf</c> computes; if the card
/// computed its row heights through those same functions, both sides of the comparison would be one
/// implementation, and a wrong slot height would cancel out into a green test. So the card maps an
/// element kind to a pixel height directly, and the arithmetic the criterion checks lives on the
/// other side of the comparison.</para>
///
/// <para><b>Empty rows are zero tall.</b> A surface whose rows are 1 and 5 looks identical to one
/// whose rows are 1 and 2: <c>row</c> orders elements, it does not space them.</para>
///
/// <para><b>This class holds a control tree, so it is stateful.</b> That state changes through five
/// methods and no others, and the list is closed on purpose:</para>
///
/// <code>
/// welcome           -> SetDocument + SetValue per key
/// surface           -> SetDocument
/// delta, value      -> SetValue
/// delta, removed    -> RemoveValue
/// delta, appended   -> AppendValue
/// cleared           -> ClearAll
/// </code>
///
/// <para><b>The renderer's change surface is exactly the wire protocol's change surface.</b> That
/// correspondence is the rule to test any sixth method against: if a method here has no message
/// behind it, either the protocol is missing something or the method is the renderer growing an
/// opinion it was not given. A sixth method added because "it would also be convenient" is how that
/// rule stops being true without anyone noticing it existed.</para>
///
/// <para><b>What this class does not do: remember values.</b> <see cref="SetDocument"/> rebuilds the
/// cells and carries over nothing that came from a key. A caller that wants the card to keep showing
/// values after a document change sends them again. The alternative — an internal
/// <c>_lastKnown</c> map — would be a second source of truth beside the protocol's, and it would go
/// stale the first time a <c>delta</c> was missed, silently. The one exception is the four editing
/// kinds, whose contents exist nowhere else (Q88): their in-progress user input is carried, because
/// no one else could resend it.</para>
/// </remarks>
public sealed class SurfaceCard : Grid
{
    /// <summary>The cells this card has built, by declared key, for the parts that carry state.</summary>
    private readonly Dictionary<string, Control> _cells = new(StringComparer.Ordinal);

    public SurfaceCard()
    {
        Width = SurfaceMetrics.CardWidth;
        Height = SurfaceMetrics.CardHeight;

        // Layout rounding OFF, and this is not a cosmetic preference.
        //
        // Avalonia rounds each grid row up to the next whole pixel by default. A row declared as 64
        // comes out at 65, and the error compounds per row rather than staying a constant: three
        // rows of 64 measure 195, not 192. The renderer's completion criterion compares Top
        // differences against SurfaceLayout's arithmetic, and that arithmetic multiplies slot counts
        // by a whole number of pixels — so with rounding on, the two sides diverge by one pixel per
        // row above the element, which grows down the card and is invisible on the first row.
        //
        // Turning it off is the honest choice rather than widening the test's tolerance: the card is
        // drawing at integer coordinates on a 390-wide canvas, so there is nothing sub-pixel to
        // round. A renderer that later adopts fractional slot heights would have to revisit this,
        // and the test below would then fail loudly instead of drifting.
        UseLayoutRounding = false;
    }

    /// <summary>
    /// Rebuilds the cells for a surface document: which cells exist and where they sit.
    /// </summary>
    /// <param name="document">The surface to lay out.</param>
    /// <remarks>
    /// <para><b>Cells, not contents.</b> This method decides how many rows there are, how tall each
    /// one is, and which element stands in which cell. It does not decide what each cell shows: a
    /// <c>Text</c>, a <c>Button</c>'s contents from the document aside, or a <c>List</c> starts empty
    /// and waits for <see cref="SetValue"/>.</para>
    ///
    /// <para><b>Why nothing is carried over.</b> A caller that keeps values across a document change
    /// sends them again — it is the party that has them, and the protocol says so: a <c>surface</c>
    /// message carries a document and no values. Carrying them here instead would mean this class
    /// held a copy of state the server owns, and a copy that is only ever refreshed by a message the
    /// card does not see is a copy that goes wrong quietly.</para>
    ///
    /// <para><b>Except for the four editing kinds, which is not a contradiction.</b> Q88 says their
    /// contents exist only in the control — nobody can resend what the user has typed. So their
    /// in-progress input travels with the rebuild, and it is a single value per control, not a copy
    /// of anything the server knows.</para>
    /// </remarks>
    public void SetDocument(KitX.ToolKit.Contract.Surface.SurfaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Captured before the tree goes away. Only the editing kinds are read: they are the ones
        // whose contents have no other home. Keyless editing elements are not in this map because
        // there is no name to carry them by, and an element with no key is not addressable anyway.
        var carriedInput = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, cell) in _cells)
            if (LocalInputOf(cell) is { } local)
                carriedInput[key] = local;

        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        _cells.Clear();

        SetCells(document.Elements, carriedInput);
    }

    /// <summary>
    /// Shows one value in the cell bound to <paramref name="key"/>, or clears it.
    /// </summary>
    /// <param name="key">The declared key.</param>
    /// <param name="value">
    /// The value, or <see langword="null"/> when the key has none. Null is a normal state, not an
    /// error: <c>welcome.values</c> carries only keys that have values, and
    /// <c>delta{removed:true}</c> produces exactly this.
    /// </param>
    /// <remarks>
    /// <para><b>The parameter is nullable for a reason worth stating.</b> A non-null
    /// <c>JsonElement</c> has no way to say "no value" — <c>default(JsonElement)</c> is
    /// <see cref="JsonValueKind.Undefined"/>, which is neither JSON null nor absent, and reaching
    /// <c>GetRawText()</c> on it throws. The protocol already models this correctly:
    /// <c>DeltaMessage.Value</c> is <c>JsonElement?</c>.</para>
    ///
    /// <para><b>SetValue replaces; it does not merge.</b> For a <c>List</c> that means the array is
    /// swapped whole and its scroll position goes to the top. That is the honest behaviour: the
    /// content is not the same content. Appending is a different message and takes a different
    /// path, <see cref="AppendValue"/>.</para>
    /// </remarks>
    public void SetValue(string key, JsonElement? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_cells.TryGetValue(key, out var cell))
            return;

        Show(cell, value);
    }

    /// <summary>
    /// Adds one entry to the end of the cell bound to <paramref name="key"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>A different path from <see cref="SetValue"/>, and the difference is the point.</b>
    /// Appending never replaces the scrolling content, so the reader's position survives because
    /// nothing was swapped underneath it. Folding append into replace would make every client
    /// re-read the whole value per appended line — O(n²) for exactly the log-shaped keys streams
    /// exist for — and, worse here, would jump the reader back to the top on every new line.</para>
    ///
    /// <para>There is deliberately no "replace but keep the scroll offset" behaviour. That is a
    /// third semantic, and a renderer that picks it silently would be answering a question the
    /// protocol never asked. If it is ever needed it needs a name of its own on the wire.</para>
    /// </remarks>
    public void AppendValue(string key, JsonElement item)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_cells.TryGetValue(key, out var cell) || cell is not ScrollViewer scroller)
            return;

        if (scroller.Content is not StackPanel rows)
            return;

        rows.Children.Add(new TextBlock { Text = Scalars(item) });
    }

    /// <summary>Removes the value shown in the cell bound to <paramref name="key"/>.</summary>
    /// <remarks>
    /// The cell stays. Removing a value removes what the cell shows, not the cell — the same rule
    /// that makes an empty <c>Text</c> occupy its slot.
    /// </remarks>
    public void RemoveValue(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_cells.TryGetValue(key, out var cell))
            return;

        Show(cell, null);
    }

    /// <summary>Empties every cell this card holds.</summary>
    /// <remarks>
    /// <para><b>The cells stay.</b> <c>cleared</c> means the toolkit's keys are gone, not that the
    /// card has no layout — a rebuild here would also lose whatever the user had typed into an
    /// editing control, which is the one thing that cannot be re-sent.</para>
    /// </remarks>
    public void ClearAll()
    {
        foreach (var cell in _cells.Values)
            Show(cell, null);
    }

    /// <summary>The current text of a cell, for tests and for carrying input across a rebuild.</summary>
    /// <remarks>
    /// Not part of the change surface — it reads, it does not mutate. It exists so a test can ask
    /// "what is on screen" without reaching into control types it would otherwise have to know.
    /// </remarks>
    public string? ShownTextOf(string key) =>
        _cells.TryGetValue(key, out var cell) ? Shown(cell) : null;

    /// <summary>Builds the cells for a document, carrying the given local inputs forward.</summary>
    private void SetCells(
        IReadOnlyList<KitX.ToolKit.Contract.Surface.SurfaceElement> elements,
        IReadOnlyDictionary<string, object?> carriedInput)
    {
        if (elements.Count == 0)
            return;

        // Slot -> tallest element standing in it. The height of a slot is decided once, by whoever
        // is tallest, so two elements sharing a row do not stack their heights.
        var slots = new SortedDictionary<int, int>();
        foreach (var element in elements)
        {
            var height = SlotsFor(element);
            if (!slots.TryGetValue(element.Row, out var existing) || height > existing)
                slots[element.Row] = height;
        }

        // Avalonia's Grid indexes rows from zero while the file numbers them from one, and it
        // numbers them consecutively — so a surface written with rows 1 and 5 gets two rows, not
        // five. This map is where "row is an ordinal" becomes true of the pixels.
        var rowIndexOf = new Dictionary<int, int>();
        var next = 0;
        foreach (var (slot, height) in slots)
        {
            rowIndexOf[slot] = next++;
            RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(height * SurfaceMetrics.SlotHeight),
            });
        }

        foreach (var element in elements)
        {
            // No null check: CreateView throws for a kind this renderer does not know, so the row
            // it would have occupied never silently becomes a gap. A document this renderer cannot
            // draw is refused as a whole rather than rendered with holes in it.
            var view = CreateView(element);

            Grid.SetRow(view, rowIndexOf[element.Row]);
            Grid.SetColumn(view, element.Column - 1);
            Children.Add(view);

            if (!string.IsNullOrEmpty(element.Key))
                _cells[element.Key] = view;

            if (carriedInput.TryGetValue(element.Key ?? string.Empty, out var local))
                RestoreLocalInput(view, local);
        }

        // Columns follow the document's declared count, which is fixed at four by the schema. Doing
        // it from the maximum column that happens to appear would make the card's width per column
        // depend on which elements a given file used.
        for (var i = 0; i < SurfaceMetrics.CardColumns; i++)
            ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
    }

    /// <summary>Puts a value into a cell, or clears it when there is none.</summary>
    /// <remarks>
    /// <b><c>CheckBox</c> is matched before <c>Button</c>, and the order carries meaning.</b>
    /// <c>CheckBox</c> derives from <c>ToggleButton</c>, which derives from <c>Button</c> — so a
    /// <c>Button</c> arm written first would swallow every tick box and quietly treat it as a label.
    /// That is the same failure this file keeps having to name: two things with the same shape, one
    /// arm answering for both.
    /// </remarks>
    private static void Show(Control cell, JsonElement? value)
    {
        switch (cell)
        {
            case TextBlock label:
                label.Text = value is { } labelValue ? Scalars(labelValue) : string.Empty;
                break;

            case CheckBox tick:
                tick.IsChecked = value is { ValueKind: JsonValueKind.True };
                break;

            case Button button:
                SetButtonValue(button, value);
                break;

            case ScrollViewer scroller:
                SetListValues(scroller, value);
                break;

            case TextBox box:
                box.Text = value is { } boxValue ? Scalars(boxValue) : string.Empty;
                break;

            case NumericUpDown spinner:
                SetNumber(spinner, value);
                break;

            case ComboBox combo:
                SetSelection(combo, value);
                break;

            default:
                throw new NotSupportedException(
                    "no way to show a value in " + cell.GetType().Name);
        }
    }

    /// <summary>What a cell currently displays, or null when it shows nothing.</summary>
    /// <remarks>
    /// <c>CheckBox</c> is matched before <c>Button</c> here for the reason given on
    /// <see cref="Show"/>: it derives from <c>Button</c>, and an arm written first would answer for
    /// it with the label branch.
    /// </remarks>
    private static string? Shown(Control cell) =>
        cell switch
        {
            TextBlock label => label.Text,
            TextBox box => box.Text,
            ScrollViewer scroller => string.Join(
                "\n", (scroller.Content as StackPanel)?.Children.OfType<TextBlock>().Select(t => t.Text) ?? []),
            CheckBox tick => tick.IsChecked == true ? "true" : "false",
            NumericUpDown spinner => spinner.Value?.ToString() ?? string.Empty,
            ComboBox combo => combo.SelectedItem?.ToString() ?? string.Empty,
            Button button => button.Content?.ToString() ?? string.Empty,
            _ => null,
        };

    /// <summary>The user's in-progress input, for the kinds whose contents live only in the control.</summary>
    /// <remarks>
    /// Null for everything else. A <c>TextBlock</c> shows a value the server has; a <c>List</c> shows
    /// entries the server has; a <c>Button</c> shows a label the document has. None of those need
    /// carrying — all three can be handed back by whoever changed the document. An editing control's
    /// half-typed text has no such owner, which is the only reason it is treated differently.
    /// </remarks>
    private static object? LocalInputOf(Control cell) =>
        cell switch
        {
            TextBox box => box.Text,
            NumericUpDown spinner => spinner.Value,
            ComboBox combo => combo.SelectedItem,
            CheckBox tick => tick.IsChecked,
            _ => null,
        };

    /// <summary>Puts a carried local input back into a freshly built editing control.</summary>
    private static void RestoreLocalInput(Control cell, object? local)
    {
        if (local is null)
            return;

        switch (cell)
        {
            case TextBox box when local is string text:
                box.Text = text;
                break;
            case NumericUpDown spinner when local is decimal number:
                spinner.Value = number;
                break;
            case ComboBox combo:
                combo.SelectedItem = local;
                break;
            case CheckBox tick when local is bool isChecked:
                tick.IsChecked = isChecked;
                break;
        }
    }

    private static void SetButtonValue(Button button, JsonElement? value) =>
        button.Content = value is { } v ? Scalars(v) : button.Content;

    private static void SetListValues(ScrollViewer scroller, JsonElement? value)
    {
        if (scroller.Content is not StackPanel rows)
            return;

        rows.Children.Clear();

        if (value is { ValueKind: JsonValueKind.Array } array)
            foreach (var item in array.EnumerateArray())
                rows.Children.Add(new TextBlock { Text = Scalars(item) });

        // Replacing content puts the reader back at the top, on purpose: the list is not the same
        // list any more. AppendValue is the path that keeps the position, because it never swaps
        // the content in the first place.
        scroller.Offset = default;
    }

    private static void SetNumber(NumericUpDown spinner, JsonElement? value) =>
        spinner.Value = value is { ValueKind: JsonValueKind.Number } v && v.TryGetDecimal(out var number)
            ? number
            : null;

    private static void SetSelection(ComboBox combo, JsonElement? value) =>
        combo.SelectedItem = value is { } v ? Scalars(v) : null;

    /// <summary>The text a JSON scalar shows as, with objects and arrays rendered as their JSON.</summary>
    /// <remarks>
    /// A cell shows text, so something has to be chosen for a value that is not text. Writing its
    /// JSON is the choice that loses nothing: a reader sees what is there rather than a type name or
    /// an empty box.
    /// </remarks>
    private static string Scalars(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText(),
        };

    /// <summary>
    /// How many slots this element's row has to be tall enough for.
    /// </summary>
    /// <remarks>
    /// <para><b>A second copy of a rule that also exists in <c>SurfaceLayout.SlotCountOf</c>, on
    /// purpose.</b> The two are supposed to agree, and the only way a test can find out whether they
    /// do is if neither one is derived from the other. A shared helper here would make the card
    /// correct by construction and the criterion vacuous.</para>
    ///
    /// <para><b><c>internal</c> rather than <c>private</c>, for the coverage test.</b> That test has to
    /// ask this renderer, for every element kind the contract declares, both "how many slots" and
    /// "can you draw it" — and it can only ask if it can see them. <c>internal</c> plus an
    /// <c>InternalsVisibleTo</c> keeps them out of the published surface: these are how the card
    /// works, not what it offers.</para>
    /// </remarks>
    internal static int SlotsFor(KitX.ToolKit.Contract.Surface.SurfaceElement element) =>
        element is KitX.ToolKit.Contract.Surface.SurfaceListElement
            ? SurfaceMetrics.ListSlotCount
            : 1;

    /// <summary>Builds the control for one element, or throws when this renderer has no view for it.</summary>
    /// <remarks>
    /// <para><b>Throwing rather than returning null, and the difference is the whole point.</b> A null
    /// return makes "not implemented" look like "implemented but empty" — the card draws a gap and
    /// nobody is told. Throwing makes it look like what it is. A renderer that does not recognise a
    /// document should refuse it, not silently drop a cell out of it.</para>
    ///
    /// <para>The coverage test asserts that every element kind the contract declares gets a non-null
    /// view, which is only meaningful if "no view" is an observable failure rather than a quiet
    /// absence. The two decisions support each other: the test can only exist because this throws,
    /// and this throwing is only safe because the test will notice if a kind is left out.</para>
    ///
    /// <para><b>Every control is exactly one slot tall, whatever it holds.</b> The empty state of a
    /// text field and the filled state of one are the same height, because the field owns a slot the
    /// way any other element does — a value decides what the cell shows, not whether the cell is
    /// there (§10.2.1.1). That holds for the four editable kinds the same way it holds for
    /// <c>Text</c>, and for them it costs nothing to check: their contents are empty by construction
    /// here, so "it occupies its slot while empty" is what every assertion about them is already
    /// saying.</para>
    ///
    /// <para><b>Select carries no candidates at this stage.</b> <c>Options</c> is declaration data the
    /// renderer is entitled to read, while <c>optionsKey</c> names a runtime value read from the
    /// session — by something upstream, not here. This stage passes neither: an empty <c>Select</c>
    /// still occupies its slot and still lands in the right place, which is the whole question being
    /// answered. How candidates reach the renderer is the next stage's problem, and keeping the key
    /// name out of these signatures is the same rule that keeps <c>value</c> out of
    /// <c>SurfaceLayout</c>: what appears in a signature, the implementation will use.</para>
    ///
    /// <para><b>List carries entries, and its control is one slot tall like everything else.</b>
    /// §10.2.1.0 separates the two heights: the slot it stands in grows to K, the control itself
    /// stays one slot and scrolls its overflow. Painting it K tall instead would put the scrollbar
    /// on the cell boundary rather than the control's, and would move "empty space below when there
    /// are few entries" from the slot (where it belongs) into the control (where it does not). Both
    /// heights coincide while only <c>Text</c> exists, which is why the distinction only shows up
    /// here.</para>
    ///
    /// <para>Entries arrive as an argument, not read from anywhere: a <c>List</c> shows a resolved
    /// array the same way a <c>Select</c> shows a resolved option set, and for the same reason —
    /// this layer receives values, it does not fetch them.</para>
    ///
    /// <para><b><c>internal</c> so the coverage and control-metrics tests can call it.</b> They need
    /// the real thing rather than a copy of its list of controls: a duplicated list is a second
    /// answer to "what does a Switch look like", and the two drift without either going red.</para>
    /// </remarks>
    internal static Control CreateView(
        KitX.ToolKit.Contract.Surface.SurfaceElement element) =>
        element switch
        {
            KitX.ToolKit.Contract.Surface.SurfaceTextElement text => Labelled(text.Key),
            KitX.ToolKit.Contract.Surface.SurfaceButtonElement button => Pressable(button.Key, button.Label),
            KitX.ToolKit.Contract.Surface.SurfaceInputElement input => Editing(
                input.Key, new global::Avalonia.Controls.TextBox()),
            KitX.ToolKit.Contract.Surface.SurfaceNumberElement number => Editing(
                number.Key, new global::Avalonia.Controls.NumericUpDown()),
            KitX.ToolKit.Contract.Surface.SurfaceSelectElement select => Editing(
                select.Key, new global::Avalonia.Controls.ComboBox()),
            KitX.ToolKit.Contract.Surface.SurfaceSwitchElement toggle => Editing(
                toggle.Key, new global::Avalonia.Controls.CheckBox()),
            KitX.ToolKit.Contract.Surface.SurfaceListElement list => Entries(list.Key),
            _ => throw new NotSupportedException(
                "no view for " + element.GetType().Name),
        };

    /// <summary>
    /// A control that only displays what it is told: one slot tall, no template of its own, empty.
    /// </summary>
    /// <remarks>
    /// <para><b><c>Text</c> is the only remaining caller.</b> It shows a value and does nothing else,
    /// which is the honest minimum for a display element and needs no template. That also makes it
    /// the one kind that measures outside a window, which is worth remembering when a failure looks
    /// like "this element is zero tall".</para>
    ///
    /// <para><b>It starts empty rather than showing its key.</b> An earlier version put the key in
    /// the label as visible placeholder text, which meant a card with no values showed a column of
    /// key names and looked populated. That is worse than looking empty: it says the toolkit has
    /// data when the only thing it has is a declaration. The key belongs in <c>Name</c>, where
    /// lookups can find it and a reader cannot.</para>
    /// </remarks>
    private static Control Labelled(string? key) => new TextBlock
    {
        Name = key ?? string.Empty,
        Text = string.Empty,
        // global:: because this assembly's own namespace ends in ".Avalonia", which shadows the
        // package's root namespace inside every file here — `Avalonia.Layout.X` would resolve to
        // `KitX.ToolKit.View.Avalonia.Layout.X` and fail to compile.
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        Height = SurfaceMetrics.SlotHeight,
    };

    /// <summary>
    /// Something the user presses: a real button, labelled and one slot tall.
    /// </summary>
    /// <remarks>
    /// <para><b>A label is not a button.</b> The two differ in more than appearance: a button is
    /// focusable, it is in the tab order, it raises a click, and assistive technology announces it as
    /// something to activate. Drawing one as a <c>TextBlock</c> produces a card where the only
    /// interactive element is unreachable by keyboard — and every layout test would still pass,
    /// because a label and a button occupy the same slot.</para>
    ///
    /// <para><b>The height is pinned for the same reason the editable kinds pin theirs</b>: a
    /// button's natural height is a theme decision, while the rule is that an element occupies one
    /// slot.</para>
    /// </remarks>
    private static Control Pressable(string? key, string label) => Editing(key, new Button
    {
        Content = label,
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
        HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
    });

    /// <summary>
    /// A control the user edits: the real widget, pinned to one slot.
    /// </summary>
    /// <param name="key">The declared key this element binds, or null.</param>
    /// <param name="control">A text box, a spinner, a drop-down, a tick box — the actual type.</param>
    /// <remarks>
    /// <para><b>These are the real controls, not stand-ins.</b> An earlier version drew all four
    /// kinds as <c>TextBlock</c>s. Those measured correctly and asserted nothing: the tests were
    /// checking that a label occupies a slot while the claim was that a text box does, so swapping in
    /// a <c>TextBox</c> would have changed every real card and broken none of the tests.</para>
    ///
    /// <para><b>The height is pinned here rather than left to the template.</b> A template's natural
    /// height is a platform and theme decision — a text box padded for touch is not one slot — while
    /// the layout rule is that every element occupies exactly one slot. Pinning it keeps the rule true
    /// across themes.</para>
    ///
    /// <para>It is also why these only measure once a window exists: a templated control with no
    /// visual root has no template to be measured from.</para>
    /// </remarks>
    private static Control Editing(string? key, Control control)
    {
        control.Name = key ?? string.Empty;
        control.Height = SurfaceMetrics.SlotHeight;
        control.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;

        return control;
    }

    /// <summary>
    /// A list's control: one slot tall, scrolling whatever does not fit.
    /// </summary>
    /// <remarks>
    /// <para><b>One slot, not K.</b> The row it sits in is K slots tall (§10.2.1.0); this control is
    /// not. It occupies the top of that row and scrolls its own overflow, so the scrollbar belongs to
    /// the control rather than sitting on the cell boundary — and the space left below a short list
    /// is the row's, not the control's.</para>
    ///
    /// <para>That the two heights are equal today, with only <c>Text</c> on the card, is exactly why
    /// the distinction needs saying: an implementation that painted the list K tall would look
    /// correct until something was drawn beside it.</para>
    ///
    /// <para><b>The rows are built directly rather than handed to an <c>ItemsControl</c>.</b> An
    /// <c>ItemsControl</c> with a bare <c>string[]</c> source produces no item containers without a
    /// data template, so its measured extent stays zero and the scroll region has nothing to scroll —
    /// which reads as "this list does not overflow" rather than as a missing template.</para>
    ///
    /// <para><b>It starts empty, and its entries arrive through <see cref="SetValue"/>.</b> An earlier
    /// version took them as a parameter of the render call, which meant the caller had to work out
    /// which of its values belonged to this control. The key is the same key either way; reading the
    /// array out of it is a lookup, not a decision.</para>
    /// </remarks>
    private static Control Entries(string? key) => new ScrollViewer
    {
        Name = key ?? string.Empty,
        Height = SurfaceMetrics.SlotHeight,
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        Content = new StackPanel(),
    };
}
