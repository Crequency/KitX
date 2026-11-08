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
    /// <remarks>
    /// Rows are created only for slots that hold something, and their heights are
    /// <see cref="SlotsFor"/> times <see cref="SurfaceMetrics.SlotHeight"/>. Slots nobody stands in
    /// contribute no row at all, which is what makes a gap in the numbering cost nothing.
    /// </remarks>
    public void Render(IReadOnlyList<KitX.ToolKit.Contract.Surface.SurfaceElement> elements)
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
            var view = CreateView(element);
            if (view is null)
                continue;

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
    /// <b>A second copy of a rule that also exists in <c>SurfaceLayout.SlotCountOf</c>, on purpose.</b>
    /// The two are supposed to agree, and the only way a test can find out whether they do is if
    /// neither one is derived from the other. A shared helper here would make the card correct by
    /// construction and the criterion vacuous.
    /// </remarks>
    private static int SlotsFor(KitX.ToolKit.Contract.Surface.SurfaceElement element) =>
        element is KitX.ToolKit.Contract.Surface.SurfaceListElement
            ? SurfaceMetrics.ListSlotCount
            : 1;

    /// <summary>Builds the control for one element, or null when this renderer has no view for it.</summary>
    /// <remarks>
    /// <para>Returning null rather than throwing keeps the card renderable while the remaining element
    /// kinds are still being built: an unknown kind costs its own row and nothing else.</para>
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
    /// </remarks>
    private static Control? CreateView(KitX.ToolKit.Contract.Surface.SurfaceElement element) =>
        element switch
        {
            KitX.ToolKit.Contract.Surface.SurfaceTextElement text => Field(text.Key, text.Key),
            KitX.ToolKit.Contract.Surface.SurfaceInputElement input => Field(input.Key, string.Empty),
            KitX.ToolKit.Contract.Surface.SurfaceNumberElement number => Field(number.Key, string.Empty),
            KitX.ToolKit.Contract.Surface.SurfaceSelectElement select => Field(select.Key, string.Empty),
            KitX.ToolKit.Contract.Surface.SurfaceSwitchElement toggle => Field(toggle.Key, string.Empty),
            _ => null,
        };

    /// <summary>One control occupying one slot.</summary>
    /// <remarks>
    /// <para>The four editable kinds and <c>Text</c> differ in what they show, not in how much room
    /// they take, so they share the sizing and nothing else. Giving each its own height is how a
    /// renderer ends up with a card whose row spacing depends on which widget a document happened to
    /// use.</para>
    ///
    /// <para><b>A control with no key is still a control.</b> <see cref="SurfaceElement.Key"/> is
    /// nullable — an element that reads nothing yet still occupies its row — so the name is left
    /// empty rather than the element being skipped. Skipping it would make the row's height depend on
    /// whether a key was written, which is the same failure as letting a value decide the layout.</para>
    /// </remarks>
    private static TextBlock Field(string? key, string shown) => new()
    {
        Name = key ?? string.Empty,
        Text = shown,
        // global:: because this assembly's own namespace ends in ".Avalonia", which shadows the
        // package's root namespace inside every file here — `Avalonia.Layout.X` would resolve to
        // `KitX.ToolKit.View.Avalonia.Layout.X` and fail to compile.
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        Height = SurfaceMetrics.SlotHeight,
    };
}
