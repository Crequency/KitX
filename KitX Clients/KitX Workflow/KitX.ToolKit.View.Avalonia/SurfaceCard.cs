using Avalonia;
using Avalonia.Controls;

namespace KitX.ToolKit.View.Avalonia;

/// <summary>
/// A 390×845 card that lays out a surface's elements by row.
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
/// whose rows are 1 and 2: <c>row</c> orders elements, it does not space them. Filling the gaps would
/// make a skipped row number look like deliberate whitespace.</para>
/// </remarks>
public sealed class SurfaceCard : Grid
{
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
    /// Builds the card for one surface document.
    /// </summary>
    /// <param name="elements">The document's elements, in any order.</param>
    /// <param name="entries">The resolved rows a <c>List</c> should show, or null for none. Entries
    /// are a value this layer receives, never one it fetches — see <see cref="CreateView"/>.</param>
    /// <remarks>
    /// Rows are created only for slots that hold something, and their heights are
    /// <see cref="SlotsFor"/> times <see cref="SurfaceMetrics.SlotHeight"/>. Slots nobody stands in
    /// contribute no row at all, which is what makes a gap in the numbering cost nothing.
    /// </remarks>
    public void Render(
        IReadOnlyList<KitX.ToolKit.Contract.Surface.SurfaceElement> elements,
        IReadOnlyList<string>? entries = null)
    {
        ArgumentNullException.ThrowIfNull(elements);

        Children.Clear();
        RowDefinitions.Clear();

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
            var view = CreateView(element, entries);

            Grid.SetRow(view, rowIndexOf[element.Row]);
            Grid.SetColumn(view, element.Column - 1);
            Children.Add(view);
        }

        // Columns follow the document's declared count, which is fixed at four by the schema. Doing
        // it from the maximum column that happens to appear would make the card's width per column
        // depend on which elements a given file used.
        for (var i = 0; i < SurfaceMetrics.CardColumns; i++)
            ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
    }

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
        KitX.ToolKit.Contract.Surface.SurfaceElement element,
        IReadOnlyList<string>? entries = null) =>
        element switch
        {
            KitX.ToolKit.Contract.Surface.SurfaceTextElement text => Labelled(text.Key, text.Key),
            KitX.ToolKit.Contract.Surface.SurfaceButtonElement button => Pressable(button.Key, button.Label),
            KitX.ToolKit.Contract.Surface.SurfaceInputElement input => Editing(
                input.Key, new global::Avalonia.Controls.TextBox()),
            KitX.ToolKit.Contract.Surface.SurfaceNumberElement number => Editing(
                number.Key, new global::Avalonia.Controls.NumericUpDown()),
            KitX.ToolKit.Contract.Surface.SurfaceSelectElement select => Editing(
                select.Key, new global::Avalonia.Controls.ComboBox()),
            KitX.ToolKit.Contract.Surface.SurfaceSwitchElement toggle => Editing(
                toggle.Key, new global::Avalonia.Controls.CheckBox()),
            KitX.ToolKit.Contract.Surface.SurfaceListElement list => Entries(list.Key, entries),
            _ => throw new NotSupportedException(
                "no view for " + element.GetType().Name),
        };

    /// <summary>
    /// A control that only displays what it is told: one slot tall, no template of its own.
    /// </summary>
    /// <remarks>
    /// <para><b><c>Text</c> is the only remaining caller.</b> It shows a value and does nothing else,
    /// which is the honest minimum for a display element and needs no template. That also makes it
    /// the one kind that measures outside a window, which is worth remembering when a failure looks
    /// like "this element is zero tall".</para>
    /// </remarks>
    private static Control Labelled(string? key, string shown) => new TextBlock
    {
        Name = key ?? string.Empty,
        Text = shown,
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
    /// which reads as "this list does not overflow" rather than as a missing template. The entries
    /// here are already resolved strings, so a <c>TextBlock</c> per entry is the whole rendering.</para>
    /// </remarks>
    private static Control Entries(string? key, IReadOnlyList<string>? entries)
    {
        var rows = new StackPanel();
        foreach (var entry in entries ?? [])
            rows.Children.Add(new TextBlock { Text = entry });

        return new ScrollViewer
        {
            Name = key ?? string.Empty,
            Height = SurfaceMetrics.SlotHeight,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = rows,
        };
    }
}
