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
    /// 64 is chosen so that a 390×845 card fits thirteen slots — enough for a typical document
    /// card, and it leaves room for the six-slot-tall <c>List</c> the layout rules reserve without
    /// that list consuming half the card.
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
