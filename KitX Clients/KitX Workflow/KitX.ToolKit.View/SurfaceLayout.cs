using KitX.ToolKit.Contract.Surface;

namespace KitX.ToolKit.View;

/// <summary>
/// How tall a slot is and where it lands — in slots, never in pixels.
/// </summary>
/// <remarks>
/// <para><b>Why this type lives in this assembly (Q86).</b> ADR-0014 D5 forbids
/// <c>KitX.ToolKit.View</c> from referencing <c>KitX.ToolKit</c>, and the renderer has to consult
/// these functions to be judged against them. Keeping them in the implementation assembly would
/// make them unreachable from any real renderer, with tests as the only consumer — which is the
/// situation Q86 was resolved to end. Moving them to the Contract was rejected too: the contract
/// must not promise anything beyond "height belongs to the renderer".</para>
///
/// <para><b>Two quantities that look like one today.</b> Every element fills a single slot, so
/// "which row" and "how many slots down" agree for every input this code can currently receive.
/// They are separate questions, and the moment a <c>List</c> fills more than one slot they diverge
/// — silently, because nothing today is looking at the difference.</para>
///
/// <para><b>The slot index is the file's <c>row</c>, unchanged, for every element.</b> Growing a
/// slot taller never renumbers the slots after it, which is what makes vertical order identical on
/// every renderer.</para>
///
/// <para><b>No pixel constant lives in here.</b> A renderer picks its own slot height, and picks how
/// many slots a <c>List</c> occupies; both arrive as parameters. That is the one disagreement the
/// contract leaves open, and keeping it out of the signatures is what keeps it to a single named
/// place per renderer.</para>
///
/// <para><b>SurfaceLayout takes the renderer's numbers as parameters. SurfaceMetrics states
/// them.</b> Both are needed, and the split is the point: a number declared here would be shared by
/// every renderer, which is why this type holds none.</para>
///
/// <para>The distinction matters because the test that guards this type asserts "no constant field
/// exists" — and that assertion would be wrong if pointed at <c>SurfaceMetrics</c>, whose whole job
/// is to declare them. So the rule is not "constants are suspect"; it is <b>"a shared layer may not
/// decide a number that belongs to one renderer"</b>. Moving <c>SurfaceMetrics</c>' constants in here
/// to "unify" them would not make the library tidier — it would give every renderer the same density
/// and take away the freedom §10.2.2 grants them.</para>
/// </remarks>
public static class SurfaceLayout
{
    /// <summary>
    /// How many slots tall this element's slot grows.
    /// </summary>
    /// <param name="element">The element to measure.</param>
    /// <param name="listSlots">How many slots a <c>List</c> fills — the renderer's choice, per
    /// §10.2.1. It arrives as a parameter rather than as a constant here because a denser renderer
    /// (a desktop card) may pick a different number, and the file must not have to change.</param>
    /// <remarks>
    /// <para><b>Returns a count of slots, never pixels.</b> The unit lives in the name because it is
    /// invisible today: with every element at one slot, an implementation returning <c>96</c>
    /// instead of <c>1</c> would still work at every current call site, and would only fail once a
    /// <c>List</c> filled more than one slot.</para>
    ///
    /// <para><b>Takes neither a value nor a constant.</b> A <c>value</c> parameter would invite the
    /// implementation to read the data, which is precisely what makes layout depend on it. The
    /// pixel choice and the <c>List</c> slot count are both caller-supplied, so nothing here is a
    /// number this layer made up.</para>
    /// </remarks>
    public static int SlotCountOf(SurfaceElement element, int listSlots)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(listSlots);

        return element is SurfaceListElement ? listSlots : 1;
    }

    /// <summary>Which slot the element sits in — always the row the file declared.</summary>
    /// <remarks>
    /// Not computed from heights: it is <see cref="SurfaceElement.Row"/> itself. It exists as a
    /// named member so the invariant has somewhere to be asserted against, instead of living as
    /// "we happen to use Row everywhere".
    /// </remarks>
    public static int SlotIndex(SurfaceElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.Row;
    }

    /// <summary>How many slots one row occupies — as tall as the tallest element standing in it.</summary>
    /// <param name="elements">Every element on the card.</param>
    /// <param name="slot">The slot number to measure.</param>
    /// <param name="listSlots">How many slots a <c>List</c> fills (the renderer's choice).</param>
    public static int SlotHeightOf(IReadOnlyList<SurfaceElement> elements, int slot, int listSlots)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var height = 0;
        foreach (var element in elements)
        {
            if (SlotIndex(element) != slot)
                continue;

            var count = SlotCountOf(element, listSlots);
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
    /// <param name="rowHeightPx">What one slot is worth in pixels.</param>
    /// <param name="listSlots">How many slots a <c>List</c> fills (the renderer's choice).</param>
    /// <remarks>
    /// <para><b>Measures the slots above the target, not through it.</b> A slot grows to hold its
    /// tallest element, so the elements above are what push the target down. The target's own
    /// height decides where the next slot starts and nothing else.</para>
    ///
    /// <para><b>Using <c>row × rowHeightPx</c> instead is correct for exactly as long as every
    /// element fills one slot</b> — every element written today — which is why the difference needs
    /// a synthetic input to become visible.</para>
    /// </remarks>
    public static int PixelTopOf(
        IReadOnlyList<SurfaceElement> elements, SurfaceElement target, int rowHeightPx, int listSlots)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(target);

        var slotsAbove = 0;
        for (var slot = 1; slot < SlotIndex(target); slot++)
            slotsAbove += SlotHeightOf(elements, slot, listSlots);

        return slotsAbove * rowHeightPx;
    }
}
