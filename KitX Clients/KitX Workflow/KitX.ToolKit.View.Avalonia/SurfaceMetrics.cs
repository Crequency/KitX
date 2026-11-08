namespace KitX.ToolKit.View.Avalonia;

/// <summary>
/// The one place that decides how tall a grid slot is.
///
/// The renderer's completion criterion says "the renderer picks a slot height, then the
/// difference between any two elements' Top values in the visual tree equals the difference
/// <c>SurfaceLayout.PixelTopOf</c> computes with that same height". For a test to do that
/// comparison it has to get the height from the renderer rather than restate it, so this is a
/// public constant and not a literal buried in a template.
///
/// It is a constant and not a setting on purpose. A configurable slot height would let two
/// surfaces render at different densities, and "row 5 is 200px down" would stop being a statement
/// about the document. Card density belongs to the platform (a phone draws tighter than a
/// desktop); the document only says which row comes first.
/// </summary>
public static class SurfaceMetrics
{
    /// <summary>
    /// Height of one grid slot, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// <para>64 is chosen so that a 390×845 card fits thirteen slots — enough for a typical document
    /// card, and it leaves room for the six-slot-tall <c>List</c> the layout rules reserve without
    /// that list consuming half the card.</para>
    ///
    /// <para><b>This value is only observable when layout rounding is off.</b> Avalonia's
    /// <c>Layoutable.UseLayoutRounding</c> defaults to <c>true</c>, and under it a <c>Grid</c> row
    /// declared at 64 measures 65 — every row, so element tops come out at 0/65/130/195, not
    /// 0/64/128. <c>StackPanel</c> and <c>Canvas</c> are unaffected, which is what makes this easy to
    /// blame on the measurement rather than the platform.</para>
    ///
    /// <para><b>Rounding also swallows non-integer heights.</b> Under rounding, 63.5 and 64.4 both
    /// measure 65. So changing this number and watching the tests is not a way to learn what it is:
    /// inside a rounded <c>Grid</c> the slot height cannot be observed at all — not "measured
    /// imprecisely", but genuinely not the quantity being read.</para>
    ///
    /// <para><b>This renderer declares an integer slot height. That is a choice, not a
    /// requirement:</b> if this ever becomes 63.5, the rounding note above is what makes the failure
    /// loud instead of silent. <c>UseLayoutRounding = false</c> is what turns it from an off-by-one
    /// into the right number.</para>
    ///
    /// <para><b>That is why loosening the tolerance in a test is the wrong repair.</b> With a 64/65
    /// ratio a 1.5px tolerance happens to cover one row and stop covering ten; with a 63.5 slot it
    /// sits exactly on the boundary and covers nothing. Whoever widens the tolerance will try one
    /// slot height and draw a conclusion that does not hold for the others. If a Top difference
    /// comes out one pixel off, the question to ask is why the number is not what was declared —
    /// <see cref="SurfaceCard"/> answers it by turning rounding off.</para>
    /// </remarks>
    public const double SlotHeight = 64;

    /// <summary>
    /// Width of the card, in device-independent pixels.
    /// </summary>
    public const double CardWidth = 390;

    /// <summary>
    /// Height of the card, in device-independent pixels.
    /// </summary>
    public const double CardHeight = 845;

    /// <summary>
    /// How many columns a card has, per the schema's <c>columns</c> field.
    /// </summary>
    public const int CardColumns = 4;

    /// <summary>
    /// How many slots a <c>List</c> occupies, per the Surface design's §10.2.1 rules.
    /// </summary>
    /// <remarks>
    /// The renderer fixes this; the document cannot ask for a different number. That is what keeps
    /// "a List grows with its data" from being expressible — see SurfaceLayout's SlotCountOf.
    /// </remarks>
    public const int ListSlotCount = 6;
}
