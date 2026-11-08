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
    /// Returning null rather than throwing keeps the card renderable while the remaining element
    /// kinds are still being built: an unknown kind costs its own row and nothing else.
    /// </remarks>
    private static Control? CreateView(KitX.ToolKit.Contract.Surface.SurfaceElement element) =>
        element switch
        {
            KitX.ToolKit.Contract.Surface.SurfaceTextElement text => new TextBlock
            {
                Name = text.Key,
                Text = text.Key,
                // global:: because this assembly's own namespace ends in ".Avalonia", which shadows
                // the package's root namespace inside every file here — `Avalonia.Layout.X` would
                // resolve to `KitX.ToolKit.View.Avalonia.Layout.X` and fail to compile.
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                Height = SurfaceMetrics.SlotHeight,
            },
            _ => null,
        };
}
