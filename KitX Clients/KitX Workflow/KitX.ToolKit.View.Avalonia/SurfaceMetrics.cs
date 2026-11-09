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
    /// How tall a single-slot control is drawn, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// <para><b>A slot is 64 and a control is 32, and the 32 sits centred in the 64.</b> The slot is
    /// the row's; the control is the element's. Q100 (2026-11-09) changed the alignment from
    /// <c>Top</c> to <c>Center</c> after the user looked at a render of <c>30-light</c> and said
    /// "vertically not filling, 32 centred is fine". That overruled §10.2.1.0's earlier "the control
    /// is always one slot tall and <c>VerticalAlignment = Top</c>" — a rule change, not a bug fix.</para>
    ///
    /// <para><b>32 is not a new number, it is what Fluent desktop controls already are.</b>
    /// Measured on this renderer: <c>TextBox</c>, <c>NumericUpDown</c>, <c>ComboBox</c> and
    /// <c>CheckBox</c> all report a natural height of 32 — see
    /// <c>EditingControlMetricsTests.Every_editable_control_fits_within_one_slot_at_its_natural_height</c>.
    /// So the layout is not compressing them; a filling control would be stretching them.</para>
    ///
    /// <para><b>What this does not change.</b> How many slots an element occupies is unchanged
    /// (<c>Text</c> 1, <c>List</c> K), and "slot height does not depend on data" is unchanged — this
    /// number is a renderer constant, not something computed from a value. Only the alignment
    /// inside the slot changed.</para>
    /// </remarks>
    public const double FieldHeight = 32;

    /// <summary>
    /// How far a single-slot control sits below its cell's top edge, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// <para><b>This is Q100's acceptance criterion, not its prose.</b> The verdict says the control
    /// is 32 tall; its own criterion says <c>Top − cellTop == (SlotHeight − 32) / 2</c>, which is
    /// 16 for every element. Those two agree on a one-slot row and disagree on a K-slot one, which
    /// is why the difference is worth naming.</para>
    ///
    /// <para><b>The prose said <c>VerticalAlignment = Center</c>, and that is wrong for a
    /// multi-slot cell.</b> Centring is relative to the whole cell, so a button sharing a row with
    /// a six-slot list is pushed to 176px down — the middle of the list it sits beside, not the top
    /// of the row. Measured: <c>Moving_one_button_to_its_own_row_lowers_it_and_leaves_the_others</c>
    /// expects 208 and <c>Center</c> produced 368. The criterion is the one that keeps "the top of
    /// the row" meaning the top of the row, so it is the one implemented here.</para>
    ///
    /// <para>16 is <c>(SlotHeight − FieldHeight) / 2</c>, written out so that changing either
    /// constant moves this one with it rather than leaving a magic number behind.</para>
    /// </remarks>
    public const double FieldInset = (SlotHeight - FieldHeight) / 2;

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
