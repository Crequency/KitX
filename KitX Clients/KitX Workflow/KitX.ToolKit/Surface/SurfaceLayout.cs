using KitX.ToolKit.Contract.Surface;

namespace KitX.ToolKit.Surface;

/// <summary>
/// How tall a slot is and where it lands — in slots, never in pixels.
/// </summary>
/// <remarks>
/// <para><b>Two quantities that look like one today.</b> Every element fills a single slot, so
/// "which row" and "how many slots down" agree for every input this code can currently receive.
/// They are separate questions, and the moment a <c>List</c> fills more than one slot they diverge
/// — silently, because nothing today is looking at the difference.</para>
///
/// <para><b>The slot index is the file's <c>row</c>, unchanged, for every element.</b> Growing a
/// slot taller never renumbers the slots after it, which is what makes vertical order identical on
/// every renderer.</para>
///
/// <para><b>Pixel constants stay in here.</b> A renderer may pick different ones; that is the one
/// disagreement the contract leaves open, and keeping it out of the signatures is what keeps it to
/// a single named place.</para>
/// </remarks>
internal static class SurfaceLayout
{
    /// <summary>
    /// How many slots tall this element's slot grows.
    /// </summary>
    /// <remarks>
    /// <para><b>Returns a count of slots, never pixels.</b> The unit lives in the name because it is
    /// invisible today: with every element at one slot, an implementation returning <c>96</c>
    /// instead of <c>1</c> would still work at every current call site, and would only fail once a
    /// <c>List</c> filled more than one slot.</para>
    ///
    /// <para><b>Takes neither a value nor a constant.</b> A <c>value</c> parameter would invite the
    /// implementation to read the data, which is precisely what makes layout depend on it; a
    /// constant parameter would move a renderer's pixel choice into the layer the file speaks for.
    /// The signature is the specification here, and it is the harder of the two.</para>
    /// </remarks>
    internal static int SlotCountOf(SurfaceElement element)
        => element is SurfaceListElement ? ListSlotCount : 1;

    /// <summary>
    /// How many slots a <c>List</c> fills, whatever number of entries it holds.
    /// </summary>
    /// <remarks>
    /// <para><b>Independent of the data on purpose.</b> A list that grew with its contents would
    /// push everything below it down, and then a value would decide vertical order instead of
    /// <c>row</c>. A short list leaves its spare slots blank rather than shrinking: a blank gap is
    /// recoverable, unpredictable layout is not.</para>
    ///
    /// <para>Six is a rendering choice, not a contract term. A renderer that scrolls the list
    /// internally may pick another number, and doing so changes pixels only.</para>
    /// </remarks>
    internal const int ListSlotCount = 6;

    /// <summary>Which slot the element sits in — always the row the file declared.</summary>
    /// <remarks>
    /// Not computed from heights: it is <see cref="SurfaceElement.Row"/> itself. It exists as a
    /// named member so the invariant has somewhere to be asserted against, instead of living as
    /// "we happen to use Row everywhere".
    /// </remarks>
    internal static int SlotIndex(SurfaceElement element) => element.Row;

    /// <summary>How many slots one row occupies — as tall as the tallest element standing in it.</summary>
    /// <param name="elements">Every element on the card.</param>
    /// <param name="slot">The slot number to measure.</param>
    internal static int SlotHeightOf(IReadOnlyList<SurfaceElement> elements, int slot)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var height = 0;
        foreach (var element in elements)
        {
            if (SlotIndex(element) != slot)
                continue;

            var count = SlotCountOf(element);
            if (count > height)
                height = count;
        }

        return height;
    }

    /// <summary>
    /// Pixel distance from the card's top edge to the element's own top edge.
    /// </summary>
    /// <param name="elements">Every element on the card — needed for slot heights, never for
    /// values, which is why no value is supplied.</param>
    /// <param name="target">The element to locate.</param>
    /// <param name="rowHeightPx">What one slot is worth in pixels — the single pixel quantity a
    /// caller supplies. Every other pixel constant this method needs is its own.</param>
    /// <remarks>
    /// <para><b>Measures the slots above the target, not through it.</b> A slot grows to hold its
    /// tallest element, so the elements above are what push the target down. The target's own
    /// height decides where the next slot starts and nothing else.</para>
    ///
    /// <para><b>Using <c>row × rowHeightPx</c> instead is correct for exactly as long as every
    /// element fills one slot</b> — every element written today — which is why the difference needs
    /// a synthetic input to become visible.</para>
    /// </remarks>
    internal static int PixelTopOf(
        IReadOnlyList<SurfaceElement> elements, SurfaceElement target, int rowHeightPx)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(target);

        var slotsAbove = 0;
        for (var slot = 1; slot < SlotIndex(target); slot++)
            slotsAbove += SlotHeightOf(elements, slot);

        return slotsAbove * rowHeightPx;
    }
}
