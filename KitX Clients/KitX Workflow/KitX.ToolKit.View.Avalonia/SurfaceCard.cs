using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Contract.View;
using System.Diagnostics;
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
/// <para><b>This class holds a control tree, so it is stateful.</b> Every change comes in through
/// <see cref="Apply"/>, which is the one place a message becomes an edit:</para>
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
/// correspondence is the rule to test any sixth change method against: if a method here has no
/// message behind it, either the protocol is missing something or the renderer is growing an opinion
/// it was not given. A sixth method added because "it would also be convenient" is how that rule
/// stops being true without anyone noticing it existed.</para>
///
/// <para><b>Why there is no <c>From(WelcomeMessage)</c> factory.</b> A factory beside a message
/// entry point is the same action with two doors, and two doors drift: whatever the factory forgot,
/// the other one would not report. One entry point also makes the protocol's one gap visible instead
/// of documented — an <c>Apply(surfaceMessage)</c> empties every key-bound cell, because that
/// message carries a document and no values. That is a property of the protocol, and here it is
/// something a test can assert rather than a sentence in a comment.</para>
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

    /// <summary>Cells that declare a <c>name</c>: the ones that can contribute to an event.</summary>
    private readonly Dictionary<string, Control> _names = new(StringComparer.Ordinal);

    /// <summary>Selects whose candidates come from a key, grouped by that key.</summary>
    /// <remarks>
    /// <para>Grouped rather than one-per-key because one list can feed several cells, and a
    /// dictionary of single cells would silently update only the last one registered.</para>
    ///
    /// <para><b>A list with static <c>options</c> never appears here.</b> Two candidate sources for
    /// one control is the disagreement this map exists to make impossible.</para>
    /// </remarks>
    private readonly Dictionary<string, List<ComboBox>> _optionSources = new(StringComparer.Ordinal);

    /// <summary>The authoritative value of every <c>Select</c> cell, by declared key.</summary>
    /// <remarks>
    /// <para><b>A <c>ComboBox</c> cannot hold this value, which is why it lives here.</b> Measured in
    /// Avalonia 11.3.13: assigning <c>"a"</c> while the list contains it works; swapping
    /// <c>ItemsSource</c> to a list that does not contain it sets <c>SelectedItem</c> to null; and
    /// assigning <c>"a"</c> again while it is still absent leaves it null. The control fuses "the
    /// value" with "a row of the list" and refuses the former when the latter is missing.</para>
    ///
    /// <para>The value therefore outlives the list, and the control shows it when it can. This is not
    /// a second source of truth: the <c>ComboBox</c> never was one for this kind. It is the same
    /// arrangement the server has, where a value exists whether or not any view can draw it.</para>
    ///
    /// <para><b>Keyed by <c>name</c>, not by <c>key</c>.</b> An editing element carries no key at
    /// all: it is not showing a stored value, it is where a value comes from. The name is the only
    /// stable identity such an element has, and it is the same word the value uploads under.</para>
    ///
    /// <para>Written from two places — the person picking a row, and a rebuild carrying that pick
    /// forward — and both go through <c>ShowSelection</c>, which owns the single judgement of whether
    /// the value is currently expressible in the list. A value arriving from the session cannot write
    /// here: a <c>Select</c> has no key, so no <c>SetValue</c> reaches it.</para>
    /// </remarks>
    private readonly Dictionary<string, string> _selection = new(StringComparer.Ordinal);

    // The class names this renderer puts on the controls it builds. Each one is half of a fact whose
    // other half is a selector in Themes/KitX.axaml; that file ships inside this assembly precisely so
    // the two halves cannot drift into different repositories. A test walks a full card's visual tree
    // and asserts every one of these has a selector, which is what turns "somebody renamed a string"
    // from silent into red.
    internal const string ThemedCard = "kitx-card";
    internal const string ThemedText = "kitx-text";
    internal const string ThemedField = "kitx-field";
    internal const string ThemedListGroup = "kitx-list-group";

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

        Classes.Add(ThemedCard);
    }

    /// <summary>
    /// Applies one message from the view protocol, which is the only way this card changes.
    /// </summary>
    /// <param name="message">A server-to-client message.</param>
    /// <exception cref="NotSupportedException">
    /// The message travels the other way. A client-to-server message reaching a renderer means the
    /// wiring is wrong, and throwing says so at the point of the mistake — a <c>default: break</c>
    /// would turn that same mistake into "the interface does nothing".
    /// </exception>
    /// <remarks>
    /// <para><b>This method caches nothing.</b> It translates one message into one of the five
    /// changes and returns. Any state kept here would make the card a second
    /// <c>ToolkitView</c> — and the assertion that a document change leaves key-bound cells empty is
    /// what keeps it from happening by accident.</para>
    ///
    /// <para><b>An applied <c>surface</c> message empties every key-bound cell.</b> That is not a
    /// defect in this method: the message carries a document and no values, so after rebuilding there
    /// is nothing to show until the caller sends values again. The gap is the protocol's, and this is
    /// where it becomes assertable rather than merely written down.</para>
    /// </remarks>
    public void Apply(ViewMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        switch (message)
        {
            case WelcomeMessage welcome:
                SetDocument(welcome.Surface);

                foreach (var (key, value) in welcome.Values)
                    SetValue(key, value);

                break;

            case SurfaceMessage surface:
                SetDocument(surface.Surface);
                break;

            case DeltaMessage { Value: null } removal:
                RemoveValue(removal.Key);
                break;

            case DeltaMessage { Appended: true } append:
                AppendValue(append.Key, append.Value!.Value);
                break;

            case DeltaMessage delta:
                SetValue(delta.Key, delta.Value);
                break;

            case ClearedMessage:
                ClearAll();
                break;

            // The other four travel client to server, or are replies to something this side sent.
            case ConnectMessage:
            case CommandMessage:
            case AckedReply:
            case RejectedReply:
            case ListMessage:
            case ToolkitsMessage:
            default:
                throw new NotSupportedException(
                    $"a renderer does not consume {message.GetType().Name}; it travels the other way, "
                    + "or it is a reply to a request this side did not make");
        }
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

        // Captured before the tree goes away. Only the editing kinds contribute: they are the ones
        // whose contents have no other home. They are read through _names, not _cells, because an
        // editing element has no key — its name is the only identity it has, and it is the same word
        // its value uploads under. A cell that declares neither is unaddressable and is not carried.
        var carriedInput = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, cell) in _names)
            if (LocalInputOf(name, cell) is { } local)
                carriedInput[name] = local;

        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        _cells.Clear();
        _names.Clear();
        _selection.Clear();

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

        if (ReplaceCandidates(key, value))
            return;

        if (!_cells.TryGetValue(key, out var cell))
            return;

        Show(key, cell, value);
    }

    /// <summary>
    /// Swaps the candidate list of every <c>Select</c> that takes its options from <paramref name="key"/>.
    /// </summary>
    /// <returns>True when the key feeds at least one list, so the caller should stop here.</returns>
    /// <remarks>
    /// <para><b>The value does not change.</b> A session list refreshing is not a statement about
    /// which session the person picked; it only changes what the drop-down can offer. Clearing the
    /// selection here would make the renderer decide that the pick is no longer valid, which is the
    /// server's call, not the renderer's.</para>
    ///
    /// <para><b>A list that declares static <c>options</c> is not touched.</b> Two sources for one
    /// list would mean the validator and the renderer could disagree about what it offers, so the
    /// static list wins and the key is ignored entirely — the same rule the validator enforces.</para>
    /// </remarks>
    private bool ReplaceCandidates(string key, JsonElement? value)
    {
        if (!_optionSources.TryGetValue(key, out var lists))
            return false;

        var candidates = value is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().Select(Scalars).ToArray()
            : [];

        foreach (var list in lists)
        {
            // The swap itself raises SelectionChanged — the control clears a selection that the new
            // list does not contain — so the guard has to cover it, not just the projection that
            // follows. Wrapping only ShowSelection left the echo of the swap reaching the record and
            // writing "" over the pick.
            var wasSilent = _projecting;

            _projecting = true;

            try
            {
                list.ItemsSource = candidates;
            }
            finally
            {
                _projecting = wasSilent;
            }

            // Re-apply what the cell holds so a value that is still offered becomes a selection
            // again, and one that is not becomes a placeholder. Without this the control would be
            // showing whatever survived the swap.
            ShowSelection(list, _selection.TryGetValue(list.Name ?? string.Empty, out var chosen) ? chosen : null);
        }

        return true;
    }

    /// <summary>What a <c>Select</c> currently offers, in order. Used by tests and diagnostics.</summary>
    internal IReadOnlyList<string> CandidatesOf(string key) =>
        _optionSources.TryGetValue(key, out var lists) && lists.Count > 0 && lists[0].ItemsSource is { } source
            ? source.Cast<object?>().Select(item => item?.ToString() ?? string.Empty).ToArray()
            : [];

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

        if (_optionSources.TryGetValue(key, out var lists))
        {
            foreach (var list in lists)
                list.ItemsSource = Array.Empty<string>();

            return;
        }

        if (!_cells.TryGetValue(key, out var cell))
            return;

        Show(key, cell, null);
    }

    /// <summary>Empties every cell this card holds.</summary>
    /// <remarks>
    /// <para><b>The cells stay.</b> <c>cleared</c> means the toolkit's keys are gone, not that the
    /// card has no layout — a rebuild here would also lose whatever the user had typed into an
    /// editing control, which is the one thing that cannot be re-sent.</para>
    /// </remarks>
    public void ClearAll()
    {
        foreach (var (key, cell) in _cells)
            Show(key, cell, null);
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

        // One row of the card, and the elements that stand in it in the order the file lists them.
        //
        // The order used to be carried by each element's `column`; since Q101 there is no column,
        // and the array order is the only thing that says "left to right". It was always there —
        // the file has always been a list — but nothing read it, so a document and a shuffled copy
        // of it drew the same card.
        var rows = new SortedDictionary<int, List<SurfaceElement>>();
        foreach (var element in elements)
        {
            if (!rows.TryGetValue(element.Row, out var row))
                rows[element.Row] = row = [];

            row.Add(element);
        }

        // Avalonia's Grid indexes rows from zero while the file numbers them from one, and it
        // numbers them consecutively — so a surface written with rows 1 and 5 gets two rows, not
        // five. This map is where "row is an ordinal" becomes true of the pixels.
        var rowIndexOf = new Dictionary<int, int>();
        var next = 0;
        foreach (var (slot, row) in rows)
        {
            rowIndexOf[slot] = next++;

            // The tallest element in a row decides that row's height, so two elements sharing a row
            // do not stack their heights.
            var height = row.Max(SlotsFor);
            RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(height * SurfaceMetrics.SlotHeight),
            });
        }

        foreach (var (slot, row) in rows)
            Children.Add(BuildRow(row, rowIndexOf[slot]));

        return;

        // Each row is its own Grid, and that is load-bearing rather than tidy.
        //
        // A card-wide Grid has one set of ColumnDefinitions shared by every row, so dividing a row
        // by its own weights would mean picking a column count that works for all of them at once —
        // the least common multiple of the weights used anywhere in the file. That says the "one
        // part" of a 1:2 row and the "one part" of a 1:1 row are the same quantity, which is exactly
        // what per-row normalisation denies. A row-local Grid has no such question: it divides the
        // width it is given by the weights it is given.
        Grid BuildRow(IReadOnlyList<SurfaceElement> rowElements, int rowIndex)
        {
            var rowGrid = new Grid
            {
                ColumnSpacing = SurfaceMetrics.FieldGap,
            };

            // A weight of `w` becomes `w` star shares, and Avalonia divides the space left after
            // ColumnSpacing among them — which is the order the design requires: take the gaps out
            // first, then divide what remains. Summing the weights here would be arithmetic the
            // Grid already does, and a second copy of it is a second thing that can disagree.
            foreach (var element in rowElements)
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition(element.Weight, GridUnitType.Star));

            Grid.SetRow(rowGrid, rowIndex);

            var column = 0;
            foreach (var element in rowElements)
                rowGrid.Children.Add(BuildCell(element, column++));

            return rowGrid;
        }

        Control BuildCell(SurfaceElement element, int column)
        {
            // No null check: CreateView throws for a kind this renderer does not know, so the cell
            // it would have occupied never silently becomes a gap. A document this renderer cannot
            // draw is refused as a whole rather than rendered with holes in it.
            var view = CreateView(element);

            Grid.SetColumn(view, column);

            // Only the two kinds that show a stored value are registered here. An editing element
            // has no key at all: it is not showing a value, it is where a value comes from, and
            // registering one would let SetValue and ClearAll write into what the person is typing.
            if (!string.IsNullOrEmpty(element.Key))
                _cells[element.Key] = view;

            // A Select's static candidates are set now; its runtime ones arrive as a value under
            // optionsKey, which is the same array the session carries. Both are candidates — the
            // difference is only who hands them over.
            if (view is ComboBox chooser && element is SurfaceSelectElement picker)
            {
                chooser.ItemsSource = picker.Options;
                chooser.PlaceholderText = string.Empty;

                // The pick is remembered under the element's name, and replayed whenever the control
                // is rebuilt or its candidates change. That is the only place a Select's value lives:
                // the control refuses to hold a value its list cannot express, so trusting it would
                // lose the pick precisely when the list changed under the person.
                if (!string.IsNullOrEmpty(element.Name))
                {
                    chooser.Name = element.Name;
                    chooser.SelectionChanged += (sender, _) => RememberSelection((ComboBox)sender!);
                    ShowSelection(chooser, _selection.TryGetValue(element.Name, out var remembered) ? remembered : null);
                }

                // Static wins: a picker that declares its own options is never fed by a key, so the
                // validator and the renderer cannot end up describing different lists.
                if (picker.Options.Count == 0 && !string.IsNullOrEmpty(picker.OptionsKey))
                {
                    if (!_optionSources.TryGetValue(picker.OptionsKey, out var feeding))
                        _optionSources[picker.OptionsKey] = feeding = [];

                    feeding.Add(chooser);
                }
            }

            // Name is a separate field from Key and is not defaulted from it: a cell may show one
            // stored value and upload its input under another word, and a cell that declares neither
            // contributes nothing. Registering only named cells is what keeps a display element out
            // of every upload without an extra check later.
            if (!string.IsNullOrEmpty(element.Name))
                _names[element.Name] = view;

            // A rebuild carries the editing kinds' current input forward, keyed by name — the same
            // identity their value uploads under. They cannot be re-sent, so this is the only way
            // their content survives SetDocument.
            if (!string.IsNullOrEmpty(element.Name) && carriedInput.TryGetValue(element.Name, out var local))
                RestoreLocalInput(element.Name, view, local);

            return view;
        }
    }

    /// <summary>Puts a value into a cell, or clears it when there is none.</summary>
    /// <remarks>
    /// <para><b>Only the kinds that show a stored value have arms here.</b> A <c>TextBlock</c> and a
    /// <c>List</c> show what a key holds; a <c>Button</c> shows a label the document carries. The four
    /// editing kinds are absent on purpose — they show what the person entered, and a value from the
    /// session must never reach them. Registering one in <c>_cells</c> would make this method write
    /// into half-typed input, which is the one thing on a card that cannot be re-sent.</para>
    ///
    /// <para><b>An unregistered control reaching here is a defect, not a no-op.</b> The lookup in
    /// <see cref="SetValue"/> only finds cells that were registered, so an arm for a kind that can
    /// never be registered would be dead code wearing the costume of coverage.</para>
    /// </remarks>
    private void Show(string key, Control cell, JsonElement? value)
    {
        switch (cell)
        {
            case TextBlock label:
                label.Text = value is { } labelValue ? Scalars(labelValue) : string.Empty;
                break;

            case Button button:
                SetButtonValue(button, value);
                break;

            case ScrollViewer scroller:
                SetListValues(scroller, value);
                break;

            // Since Q99 a List's cell is the group container, not the scroller itself. The value a
            // List shows lives in the scroller inside it, so the container forwards. Reaching for
            // the child by shape rather than by name keeps this independent of what the container
            // is called.
            case Border group:
                SetListValues(group.GetVisualDescendants().OfType<ScrollViewer>().Single(), value);
                break;

            default:
                throw new NotSupportedException(
                    "no way to show a value in " + cell.GetType().Name
                    + " — an editing control is never registered for values");
        }
    }

    /// <summary>What a cell currently displays, or null when it shows nothing.</summary>
    /// <remarks>
    /// <para><b>Only the kinds registered in <c>_cells</c> answer.</b> A <c>TextBlock</c> shows what a
    /// key holds, a <c>List</c> shows its entries, a <c>Button</c> shows a label the document carries.
    /// The editing kinds are absent: their contents are not a value anybody stored, and reading them
    /// here would answer a question this method is not asked. What they hold is read by
    /// <see cref="CollectNamedValues"/> and <see cref="LocalInputOf"/>, both of which go through
    /// <c>_names</c>.</para>
    /// </remarks>
    private string? Shown(Control cell) =>
        cell switch
        {
            TextBlock label => label.Text,
            ScrollViewer scroller => ShownList(scroller),
            // Since Q99 the cell a List occupies is its group container. What the list shows is in
            // the scroller inside it, so this reads through the same shape the Show method does.
            Border group when group.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } inner
                => ShownList(inner),
            Button button => button.Content?.ToString() ?? string.Empty,
            _ => null,
        };

    private static string ShownList(ScrollViewer scroller) => string.Join(
        "\n", (scroller.Content as StackPanel)?.Children.OfType<TextBlock>().Select(t => t.Text) ?? []);

    /// <summary>
    /// Every named value on the card right now, ready to travel with an event.
    /// </summary>
    /// <remarks>
    /// <para><b>Keyed by <c>name</c>, and only elements that declare one contribute.</b> A display
    /// element with no name is not part of an upload, and a button has nothing of its own to send.
    /// Reading the control rather than the store is deliberate: what the person typed is in the
    /// control and has never been stored.</para>
    ///
    /// <para>Strings keep their text, switches keep their bool, numbers keep their decimal as a JSON
    /// number, and a <c>Select</c> keeps its recorded value — which is not always what the control
    /// shows, since a value absent from the candidates appears as a placeholder. Uploading what the
    /// control displays would silently drop such a value. Everything else is skipped — a cell whose
    /// value the renderer cannot express as JSON is not an error, it is a cell that was never meant
    /// to be uploaded.</para>
    /// </remarks>
    private IReadOnlyDictionary<string, JsonElement> CollectNamedValues()
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var (name, cell) in _names)
        {
            var value = cell switch
            {
                global::Avalonia.Controls.TextBox box => JsonSerializer.SerializeToElement(box.Text ?? string.Empty),
                global::Avalonia.Controls.NumericUpDown spinner => JsonSerializer.SerializeToElement(spinner.Value),
                global::Avalonia.Controls.ComboBox => _selection.TryGetValue(name, out var chosen)
                    ? JsonSerializer.SerializeToElement(chosen)
                    : null,
                global::Avalonia.Controls.CheckBox tick => JsonSerializer.SerializeToElement(tick.IsChecked ?? false),
                _ => (JsonElement?)null,
            };

            if (value is { } present)
                values[name] = present;
        }

        return values;
    }

    /// <summary>Records the row the person just picked, so a rebuild or a candidate swap cannot lose it.</summary>
    /// <remarks>
    /// <para><b>This is the only write that comes from the person, and until now it did not exist.</b>
    /// The handler was never wired, so <c>_selection</c> was only ever written by a rebuild carrying a
    /// previous pick forward — which meant a fresh card uploaded nothing for a <c>Select</c> no matter
    /// what was chosen on it. Every other editing kind reads its control at upload time; a
    /// <c>ComboBox</c> cannot, because it shows a placeholder whenever the list does not contain the
    /// value.</para>
    ///
    /// <para><b>Clearing the pick is recorded too.</b> <c>SelectedItem</c> going back to null is a
    /// person saying "nothing", and leaving the old value in the map would upload a pick the card is
    /// no longer showing.</para>
    /// </remarks>
    private void RememberSelection(ComboBox combo)
    {
        // A projection in progress is writing the value into the control; the control's echo of that
        // write is not the person speaking, and recording it would overwrite the very value being
        // projected. See ShowSelection.
        if (_projecting)
            return;

        if (string.IsNullOrEmpty(combo.Name))
            return;

        _selection[combo.Name] = combo.SelectedItem?.ToString() ?? string.Empty;
    }

    /// <summary>True while a value is being written into a control, so its echo is not taken as input.</summary>
    /// <remarks>
    /// Nesting is tracked rather than assumed: a candidate swap runs a projection from inside another
    /// projection, and the inner one must not clear the flag on its way out.
    /// </remarks>
    private bool _projecting;

    /// <summary>Raised when the person at the card does something the card declares.</summary>
    /// <remarks>
    /// One event per declared name, all carrying the same snapshot. See <see cref="SurfaceEvent"/>
    /// for why they are not bundled.
    /// </remarks>
    public event EventHandler<SurfaceEvent>? SomethingHappened;

    private void Raise(IReadOnlyList<string> eventNames, string? triggerId = null)
    {
        if (eventNames.Count == 0)
            return;

        var values = CollectNamedValues();

        foreach (var name in eventNames)
            SomethingHappened?.Invoke(this, new SurfaceEvent(SurfaceEvent.KindOf(name), values));
    }
    /// <summary>The user's in-progress input, for the kinds whose contents live only in the control.</summary>
    /// <remarks>
    /// <para><b>Keyed by <c>name</c>, because that is the only identity an editing element has.</b> It
    /// has no key: it is not showing a stored value, it is where a value comes from.</para>
    ///
    /// <para>Null for everything else. A <c>TextBlock</c> shows a value the server has; a <c>List</c>
    /// shows entries the server has; a <c>Button</c> shows a label the document has. None of those need
    /// carrying — all three can be handed back by whoever changed the document. An editing control's
    /// half-typed text has no such owner, which is the only reason it is treated differently.</para>
    /// </remarks>
    private object? LocalInputOf(string name, Control cell) =>
        cell switch
        {
            TextBox box => box.Text,
            NumericUpDown spinner => spinner.Value,
            ComboBox => _selection.TryGetValue(name, out var chosen) ? chosen : null,
            CheckBox tick => tick.IsChecked,
            _ => null,
        };

    /// <summary>Puts a carried local input back into a freshly built editing control.</summary>
    /// <remarks>
    /// <para><b>A carried <c>Select</c> goes through the same judgement as a value arriving from the
    /// session</b> — <see cref="ShowSelection"/>. Assigning <c>SelectedItem</c> directly was the
    /// second position of the same defect: at rebuild time the control's list is still empty, so the
    /// assignment silently produced null and the person's choice disappeared without a word.</para>
    /// </remarks>
    private void RestoreLocalInput(string name, Control cell, object? local)
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
                var carried = local as string;
                if (carried is not null)
                    _selection[name] = carried;
                ShowSelection(combo, carried);
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

    /// <summary>
    /// Makes a <c>ComboBox</c> display a value, as a selection when the list contains it and as a
    /// placeholder otherwise.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the one place that decides "is this value expressible in the list".</b> Two
    /// callers need that judgement — a rebuild carrying a pick forward, and candidates arriving —
    /// and writing it twice is how one of them ends up out of date. The person picking a row does not
    /// need it: they picked something that is by construction in the list, and
    /// <c>RememberSelection</c> records what the control now holds.</para>
    ///
    /// <para><b>The control is not asked to hold what it cannot.</b> Measured in Avalonia 11.3.13:
    /// assigning <c>SelectedItem</c> to a value absent from <c>ItemsSource</c> leaves it null, and
    /// swapping the list drops an existing selection. So when the value is not in the list it is
    /// shown as <see cref="ComboBox.PlaceholderText"/> instead, which is honest — it says "this is
    /// the value" without claiming the list contains it. The value itself lives in
    /// <c>_selection</c> throughout, so it still uploads and still survives a rebuild.</para>
    ///
    /// <para><b>The projection is made silent first, and that is not a detail.</b> Every assignment
    /// below can raise <c>SelectionChanged</c> — swapping a list drops the selection, and clearing
    /// one does too — so an unguarded projection writes the empty string over the value it is in the
    /// middle of projecting. The person's pick was "two"; the list no longer offers it; the projection
    /// says so with a placeholder; and the echo would have said "nothing was picked". Those are
    /// different facts and only one of them is true.</para>
    /// </remarks>
    private void ShowSelection(ComboBox combo, string? text)
    {
        var wasSilent = _projecting;

        _projecting = true;

        try
        {
            Project(combo, text);
        }
        finally
        {
            _projecting = wasSilent;
        }
    }

    private static void Project(ComboBox combo, string? text)
    {
        var candidates = combo.ItemsSource;

        if (text is not null && candidates is not null)
            foreach (var candidate in candidates)
                if (candidate is not null && string.Equals(candidate.ToString(), text, StringComparison.Ordinal))
                {
                    combo.SelectedItem = candidate;
                    combo.PlaceholderText = string.Empty;
                    return;
                }

        // Not in the list, or no value at all. Clearing first matters: the control refuses a foreign
        // selection but keeps an old one if it is still a member of the new list.
        combo.SelectedItem = null;
        combo.PlaceholderText = text ?? string.Empty;
    }

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
    /// <para><b>A <c>Select</c> keeps its own value, because no control there can hold it.</b> An
    /// earlier revision of this comment said candidates were "the next stage's problem", and that was
    /// wrong twice over: candidates had already arrived — <c>optionsKey</c> names a declared key, and
    /// <c>welcome.values</c> carries every declared key that has a value, so the array was in hand —
    /// and the sentence was used to justify a signature that could not have carried it. The reason it
    /// looked plausible is the rule that <c>value</c> stays out of <c>SurfaceLayout</c>, which is about
    /// <em>position</em>: a value never changes where a cell goes. Candidates change what a cell
    /// <em>shows</em>, which is the renderer's whole job.</para>
    ///
    /// <para><b>So the authoritative value of a <c>Select</c> lives in <c>_selection</c>, not in
    /// <c>ComboBox.SelectedItem</c>.</b> The other four editable kinds happen to keep their value in
    /// their control, and it is tempting to read that as a rule — it is a property of those four
    /// controls, and a <c>ComboBox</c> is the one that does not have it: it fuses "the value" with
    /// "one row of the list", so it cannot hold a value the list does not contain. Swapping the list
    /// drops the selection (measured: assigning <c>"a"</c>, then setting <c>ItemsSource</c> to
    /// <c>["c"]</c>, leaves <c>SelectedItem</c> null; assigning <c>"a"</c> again leaves it null). The
    /// selection is therefore a <em>projection</em> of the value, and when the list cannot express it
    /// the projection is a placeholder — the value still travels, which is the behaviour that matters.
    /// </para>
    ///
    /// <para>There is one judgement here and one place that makes it: <c>ShowSelection</c>. Every path
    /// that changes either the candidates or the value goes through it, so "is this value in the
    /// list" is decided once and grep finds it.</para>
    ///
    /// <para><b>List carries entries, and its control is one slot tall like everything else.</b>
    /// §10.2.1.0 separates the two heights: the slot it stands in grows to K, the control itself
    /// stays one slot and scrolls its overflow. Painting it K tall instead would put the scrollbar
    /// on the cell boundary rather than the control's, and would move "empty space below when there
    /// are few entries" from the slot (where it belongs) into the control (where it does not). Both
    /// heights coincide while only <c>Text</c> exists, which is why the distinction only shows up
    /// here.</para>
    ///
    /// <para><b>A cell is built empty and filled later.</b> Everything this method returns starts
    /// with nothing in it: a <c>List</c> gets an empty scroll panel, a <c>Text</c> an empty label, a
    /// <c>Select</c> no selection. The values arrive through the five change methods, which is where
    /// the protocol puts them — a <c>surface</c> message carries a document and no values at all, so
    /// a builder that wanted them would have nowhere to look.</para>
    ///
    /// <para><b><c>internal</c> so the coverage and control-metrics tests can call it.</b> They need
    /// the real thing rather than a copy of its list of controls: a duplicated list is a second
    /// answer to "what does a Switch look like", and the two drift without either going red.</para>
    /// </remarks>
    internal Control CreateView(
        KitX.ToolKit.Contract.Surface.SurfaceElement element)
    {
        var view = element switch
        {
            KitX.ToolKit.Contract.Surface.SurfaceTextElement text => Labelled(text.Key),
            KitX.ToolKit.Contract.Surface.SurfaceButtonElement button => Pressable(button.Key, button.Label, button.Events, button.Emphasis),
            KitX.ToolKit.Contract.Surface.SurfaceInputElement input => Editing(
                new global::Avalonia.Controls.TextBox()),
            KitX.ToolKit.Contract.Surface.SurfaceNumberElement number => Editing(
                new global::Avalonia.Controls.NumericUpDown()),
            KitX.ToolKit.Contract.Surface.SurfaceSelectElement select => Editing(
                new global::Avalonia.Controls.ComboBox()),
            KitX.ToolKit.Contract.Surface.SurfaceSwitchElement toggle => Editing(
                new global::Avalonia.Controls.CheckBox()),
            KitX.ToolKit.Contract.Surface.SurfaceListElement list => Entries(),
            _ => throw new NotSupportedException(
                "no view for " + element.GetType().Name),
        };

        // This is the only place a control gets a Name, and the only value it can be given is the
        // element's own Name. Control.Name is Avalonia's element address — what x:Name becomes at
        // runtime, what automation and styling resolve — so assigning one is saying "this element
        // can be pointed at". Q80 deleted the element id from the model; letting Control.Name carry
        // Key would have kept an address alive by borrowing another field's value.
        //
        // A surface element without a Name therefore has no address and is left unassigned — null,
        // not the empty string. "" is still an address, one whose content happens to be empty, and
        // it is reachable by FindControl("") or Name == ""; a later test could start using it as a
        // lookup key with nothing visible to say the choice was accidental.
        if (!string.IsNullOrEmpty(element.Name))
            view.Name = element.Name;

        return view;
    }

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
    private static Control Labelled(string? key)
    {
        var block = new TextBlock
        {
            Text = string.Empty,
            // global:: because this assembly's own namespace ends in ".Avalonia", which shadows the
            // package's root namespace inside every file here — `Avalonia.Layout.X` would resolve to
            // `KitX.ToolKit.View.Avalonia.Layout.X` and fail to compile.
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            Height = SurfaceMetrics.FieldHeight,
            Margin = new Thickness(0, SurfaceMetrics.FieldInset, 0, 0),
        };

        block.Classes.Add(ThemedText);
        return block;
    }

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
    private Control Pressable(string? key, string label, IReadOnlyList<string> events, string? emphasis)
    {
        var pressable = new Button
        {
            Content = label,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
        };

        // The click is wired here rather than by whoever hosts the card: the card knows which names
        // this button declares, and asking a host to read the document back to find out would mean
        // two parties deciding what a click means.
        pressable.Click += (_, _) => Raise(events);

        // Emphasis is a class, not a size. Setting a font weight or a margin here would make the three
        // values differ in Bounds, and then `row` would stop being the only thing that decides where a
        // card's contents sit. Nothing below touches geometry.
        pressable.Classes.Add(emphasis switch
        {
            "primary" => "kitx-primary",
            "danger" => "kitx-danger",
            _ => "kitx-button",
        });

        return Pin(pressable);
    }

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
    private static Control Editing(Control control)
    {
        control.Classes.Add(ThemedField);

        return Pin(control);
    }

    /// <summary>
    /// Pins a control to Q100's geometry: 32 tall, sitting <c>FieldInset</c> below the cell top.
    /// </summary>
    /// <remarks>
    /// <para><b>Q100, as its own acceptance criterion states it</b>: 32 tall, and
    /// <c>(SlotHeight - 32) / 2</c> down from the cell's top. Not <c>VerticalAlignment = Center</c>,
    /// which was Q100's prose and is wrong for a K-slot cell: a control centred in a six-slot cell
    /// lands 176px down, in the middle of the list beside it. See
    /// <see cref="SurfaceMetrics.FieldInset"/> for the arithmetic and the reasoning.</para>
    ///
    /// <para><b>Geometry only, no class.</b> A button and a field share this placement and share no
    /// appearance, so the placement lives here and each caller adds the class that names its own
    /// look. Folding them together would mean a class applied to things it does not describe.</para>
    /// </remarks>
    private static Control Pin(Control control)
    {
        control.Height = SurfaceMetrics.FieldHeight;
        control.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        control.Margin = new Thickness(0, SurfaceMetrics.FieldInset, 0, 0);

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
    ///
    /// <para><b>It sits inside a group container, and the container is what fills the row.</b> Q99:
    /// the container takes the whole K-slot area it was given; the scroller is one slot tall at the
    /// container's top. Two consequences worth naming, because both were decisions rather than
    /// incidental.</para>
    ///
    /// <para>The container wraps only the columns this element spans, not the full card width. A
    /// half-width list that looked like a full-width region would tell the reader the wrong thing
    /// about what the author declared.</para>
    ///
    /// <para>There is no title. <c>SurfaceListElement</c> carries only <c>itemTitle</c> and
    /// <c>itemSubtitle</c> — S8 ruled that out. And even if a <c>title</c> field existed, the renderer
    /// should not fill it: a heading like "扫描记录（3）" counts the entries, and counting is the one
    /// thing a renderer must not do on the author's behalf. An author who wants that heading puts a
    /// <c>Text</c> element above the list.</para>
    /// </remarks>
    private static Control Entries()
    {
        var scroller = new ScrollViewer
        {
            Height = SurfaceMetrics.SlotHeight,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel(),
        };

        // No height here: the container is placed in the Grid cell, whose RowDefinition already carries
        // K slots. Asking for a height would be this control deciding how tall the row is, which is the
        // row's job.
        //
        // The container carries a class rather than a Name now. A Name is an address, and Q101 removed
        // the idea that an element has one; what this needs is an appearance, which is a class. The
        // tests that used to find it by name go through CardProbe, which walks the tree by position.
        var group = new Border
        {
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 6, 0, 6),
            Child = scroller,
        };

        group.Classes.Add(ThemedListGroup);
        return group;
    }
}
