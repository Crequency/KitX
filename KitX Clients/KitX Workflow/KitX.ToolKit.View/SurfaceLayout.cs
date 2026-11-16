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

    /// <summary>How many slots one row occupies — at least one, and taller when something stands in it.</summary>
    /// <param name="elements">Every element on the card.</param>
    /// <param name="slot">The slot number to measure.</param>
    /// <param name="listSlots">How many slots a <c>List</c> fills (the renderer's choice).</param>
    /// <remarks>
    /// <para><b>An empty row is still a row, and still one slot tall.</b> This returned zero until
    /// 2026-11-16, on the ruling that row numbers were an order and not a distance: rows 1 and 5 drew
    /// directly above one another, exactly as 1 and 2 would. The ruling was reversed because of what
    /// it did to the gesture — drag a cell to row 2 while row 1 is empty and <i>nothing on the screen
    /// changes</i>. The action said something and the picture did not answer. Whatever the merits of
    /// the two readings, an action with no visible result is not one an editor can offer.</para>
    ///
    /// <para><b>What that means for the file.</b> <c>row: 5</c> puts an element four rows down, and
    /// rows 2, 3, and 4 are blank bands whether or not anyone wrote anything in them. That is the
    /// plain reading of a row number, and the reason the design's three objections to it are recorded
    /// as accepted costs rather than as refutations (§10.2.1.2). One of them is a real hazard: a
    /// hand-written <c>row: 100</c> asks a one-screen screen for 99 blank bands, so the editor warns
    /// when the total passes the renderer's screen height.</para>
    ///
    /// <para><b>Still the one place that answers "how tall is this row".</b> <see cref="SlotSpanOf"/>
    /// and <see cref="PixelTopOf"/> both call it, so this rule is stated once; a second implementation
    /// is how the card and the editor come to disagree about where a row starts.</para>
    /// </remarks>
    public static int SlotHeightOf(IReadOnlyList<SurfaceElement> elements, int slot, int listSlots)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var height = 1;
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
    /// How many slots tall the whole card is, counting every row number up to the last one used.
    /// </summary>
    /// <param name="elements">Every element on the card.</param>
    /// <param name="listSlots">How many slots a <c>List</c> fills (the renderer's choice).</param>
    /// <remarks>
    /// <para><b>Sums the rows in range, asking <see cref="SlotHeightOf"/> about each.</b> It holds no
    /// opinion of its own about an empty row: whatever that function answers, this adds. Since
    /// 2026-11-16 it answers one, so a document that skips from row 1 to row 5 measures four slots
    /// taller than one that goes 1, 2 — and this sum followed that decision without being edited,
    /// which is why the split was worth making.</para>
    ///
    /// <para><b>This is the one place a card's total height is computed.</b> The editor's warning
    /// "this is taller than a screen" and the renderer that draws the card must not each sum the rows
    /// themselves: two sums drift apart silently, and the drift shows up as a warning that fires on
    /// a card that fits. Pixels stay out of it for the same reason they stay out of
    /// <see cref="SlotHeightOf"/> — a slot count needs no renderer, and the comparison against one
    /// screen is the caller's, made with the caller's own pixel constant.</para>
    /// </remarks>
    public static int SlotSpanOf(IReadOnlyList<SurfaceElement> elements, int listSlots)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(listSlots);

        var last = 0;
        foreach (var element in elements)
        {
            var slot = SlotIndex(element);
            if (slot > last)
                last = slot;
        }

        var total = 0;
        for (var slot = 1; slot <= last; slot++)
            total += SlotHeightOf(elements, slot, listSlots);

        return total;
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
