using Avalonia.Controls;
using Avalonia.VisualTree;

namespace KitX.ToolKit.View.Avalonia.Test.Xunit;

/// <summary>
/// Reads a rendered card back as a list of elements, in the order a person would read them.
/// </summary>
/// <remarks>
/// <b>Why this exists at all.</b> Since Q101 a card is no longer a flat grid: <see cref="SurfaceCard"/>
/// puts each declared row inside a <see cref="Grid"/> of its own, because a row divides its width among
/// its own elements and there is no column index to place them by. That is what makes
/// <c>weight: {1, 2}</c> mean "one third of this row" in every row, rather than "one third of a column
/// set every row must share".
///
/// <para><b>What that costs a test.</b> <see cref="Avalonia.Controls.Panel.Children"/> on the card is now
/// one child per <i>row</i>, not one per element, and an element's <see cref="Avalonia.Layout.Layoutable.Bounds"/>
/// is relative to the row grid. Both facts are invisible in a passing assertion that only counts
/// children, which is why every test that used to read <c>card.Children</c> has to go through here
/// instead. A test that keeps reading <c>card.Children</c> does not fail loudly — it starts measuring
/// rows and calling them elements.</para>
///
/// <para>The order is row first, then the order the elements appear inside their row, which since Q101
/// is exactly the order the file declared them. That is the same order <c>column</c> used to express,
/// so fixtures that were written with ascending columns read the same as before.</para>
/// </remarks>
internal static class CardProbe
{
    /// <summary>The rows the card laid out, top to bottom.</summary>
    internal static IReadOnlyList<Grid> Rows(SurfaceCard card) =>
        [.. card.Children.OfType<Grid>().OrderBy(row => row.Bounds.Top)];

    /// <summary>Every element the card drew, in reading order.</summary>
    internal static IReadOnlyList<Control> Elements(SurfaceCard card) =>
        [.. Rows(card).SelectMany(row => row.Children.OfType<Control>())];

    /// <summary>The element at <paramref name="index"/> in reading order.</summary>
    internal static Control ElementAt(SurfaceCard card, int index) => Elements(card)[index];

    /// <summary>The element's top edge, measured from the card's own top.</summary>
    /// <remarks>
    /// <b>Why the row offset is added.</b> <see cref="Avalonia.Layout.Layoutable.Bounds"/> is relative to
    /// the parent panel, and the parent of an element is now its row grid rather than the card. Reading
    /// <c>Bounds.Top</c> alone would give "distance from the top of its row", which is the same number as
    /// "distance from the top of the card" for every element on the first row — so half of a two-row
    /// card would keep passing while the other half drifted by a whole row.
    /// </remarks>
    internal static double TopOf(SurfaceCard card, Control element) =>
        RowTopOf(card, element) + element.Bounds.Top;

    /// <summary>The top edge of the row the element sits in, measured from the card's own top.</summary>
    internal static double RowTopOf(SurfaceCard card, Control element)
    {
        var rows = Rows(card);

        var row = -1;

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Children.Contains(element))
            {
                row = i;
                break;
            }
        }

        if (row < 0)
            throw new InvalidOperationException(
                $"the {element.GetType().Name} is not inside any row of the card it was read from");

        var top = 0.0;

        for (var i = 0; i < row; i++)
            top += rows[i].Bounds.Height;

        return top;
    }

    /// <summary>The top edge of the <paramref name="index"/>-th row, measured from the card's own top.</summary>
    internal static double RowTopAt(SurfaceCard card, int index)
    {
        var top = 0.0;

        for (var i = 0; i < index; i++)
            top += Rows(card)[i].Bounds.Height;

        return top;
    }
}
