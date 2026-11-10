using System.Text.Json.Serialization;

namespace KitX.ToolKit.Contract.Surface;

/// <summary>
/// One card on a ToolKit's surface: rows of elements that read and write declared keys.
/// </summary>
/// <remarks>
/// <para><b>A surface is a view of keys, nothing more.</b> Every element either displays one
/// declared key or offers a way to write one. Elements carry no id — position is the identity
/// (Q80), so moving an element is an edit, not a delete-and-add. That also means the grid can be
/// sent as a whole and diffed by position.</para>
///
/// <para><b>Horizontal placement is a ratio, not a coordinate.</b> An element says which row it is
/// in and what share of that row it takes. There is no column index and no column count: a card is
/// as wide as the screen, and the only portable statement about widths is "this one gets three
/// parts of the row and that one gets one" (Q101).</para>
///
/// <para><b>Deliberately not part of the compiled artifact.</b> Retyping the layout must not
/// require unmounting the ToolKit, which is only true if compiling never reads this file
/// (ADR-0016 D2). The validator runs after compilation and is given the compiled key table; the
/// dependency does not point the other way.</para>
///
/// <para><b>A surface may reference a key that no longer exists.</b> That is a normal intermediate
/// state (delete the key, then fix the card), so loading drops the offending element and reports a
/// warning rather than failing the whole card — one missing cell beats a blank screen.</para>
/// </remarks>
public sealed record SurfaceDocument
{
    /// <summary>
    /// The layout format version this document is written in.
    /// </summary>
    /// <remarks>
    /// Checked on load. An unrecognised version is refused and treated as an empty document: a
    /// newer format may mean anything, and guessing at its meaning is how half-rendered cards
    /// happen. Empty is honest — "I cannot read this" looks the same to a renderer as "nothing
    /// declared yet", which is the safe thing for it to show.
    /// </remarks>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The version this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The horizontal layout model this document is written in.
    /// </summary>
    /// <remarks>
    /// <para><b>Fixed at 1, and required.</b> The predecessor of this field was <c>columns: 4</c>,
    /// which said how many equal columns the grid had. There is no grid any more — an element carries
    /// a <see cref="SurfaceElement.Weight"/> and a row divides its own width by the weights in it — so
    /// a field that still said "4 columns" would be describing a structure the file no longer has.
    /// The name changed with the meaning, not for decoration.</para>
    ///
    /// <para><b>It is a version probe, and its job is unchanged.</b> A build that writes a different
    /// layout model would put <c>2</c> here, and an older renderer must refuse the file rather than
    /// lay it out under the wrong rules — a mislaid card looks wrong in a way nobody can trace back
    /// to a version mismatch. So a value other than 1 is a load failure, not a hint to adapt.</para>
    ///
    /// <para><b>Absent is not the same as 1.</b> A file with no <c>layoutVersion</c> has not said
    /// which model it was written in; filling in 1 is the one guess this field exists to prevent.
    /// Loading refuses such a file — see <c>SurfaceStore</c>.</para>
    /// </remarks>
    [JsonPropertyName("layoutVersion")]
    public int LayoutVersion { get; init; } = CurrentLayoutVersion;

    /// <summary>The only layout model this build can lay out. See <see cref="LayoutVersion"/>.</summary>
    public const int CurrentLayoutVersion = 1;

    /// <summary>The elements, in the order they were declared (position, not order, decides layout).</summary>
    [JsonPropertyName("elements")]
    public IReadOnlyList<SurfaceElement> Elements { get; init; } = [];

    /// <summary>An empty document — no elements.</summary>
    /// <remarks>
    /// This is what a refused load and an absent file both hand back, because a renderer draws the
    /// same thing either way. The two are told apart by <see cref="SurfaceLoadResult.Errors"/>,
    /// which is empty only in the "no file" case.
    /// </remarks>
    public static SurfaceDocument Empty { get; } = new();
}

/// <summary>
/// Base for the seven element kinds. Deserialised by the <c>type</c> discriminator.
/// </summary>
/// <remarks>
/// <para><b>No <c>id</c>, on purpose.</b> Elements have no identity beyond their cell: the editor
/// addresses them by (row, col), and a re-layout is a rewrite of the grid rather than a diff of
/// objects. Adding ids would invite a second identity that can disagree with the position.</para>
///
/// <para><b>Row height is content's business.</b> An element says which row it sits in, never how
/// tall that row is. There is no <c>rowSpan</c> for the same reason: spanning rows would turn
/// "adapt to the screen" into a guess.</para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SurfaceTextElement), "Text")]
[JsonDerivedType(typeof(SurfaceButtonElement), "Button")]
[JsonDerivedType(typeof(SurfaceInputElement), "Input")]
[JsonDerivedType(typeof(SurfaceNumberElement), "Number")]
[JsonDerivedType(typeof(SurfaceSelectElement), "Select")]
[JsonDerivedType(typeof(SurfaceSwitchElement), "Switch")]
[JsonDerivedType(typeof(SurfaceListElement), "List")]
public abstract record SurfaceElement
{
    /// <summary>Which row the element sits in, 1-based.</summary>
    [JsonPropertyName("row")]
    public int Row { get; init; } = 1;

    /// <summary>
    /// How much of its row this element takes, relative to the others in the same row.
    /// </summary>
    /// <remarks>
    /// <para><b>Normalised within the row, not across the card.</b> "A wide input and a narrow
    /// dropdown" is an intention about one row; a card-wide unit would let row 1's weights change
    /// row 5's widths. Widths are computed as
    /// <c>available × weight_i / (weight_1 + … + weight_n)</c>, where <c>available</c> is the card's
    /// inner width minus the gaps between the row's <c>n</c> elements.</para>
    ///
    /// <para><b>A positive integer, 1 by default.</b> Not a fraction: <c>1.5</c> serialises as
    /// <c>1.5</c> on one machine and <c>1.5000000001</c> on the next, so two identical-looking files
    /// would not be identical. And nobody writes <c>1.5</c> anyway — they write 3:2.</para>
    ///
    /// <para><b>Order within a row is the order in the file.</b> There is no <c>column</c> to say
    /// otherwise, so the array carries that meaning now. It was always sorted; before, nothing read
    /// it.</para>
    ///
    /// <para><b>A single element alone in its row takes the whole width</b> — 1/1 is 100%. There is
    /// deliberately no "hug the content and float right" mode: that would be a second layout rule,
    /// and welding "visual weight" to "horizontal placement" is how <c>Bind</c>/<c>BindEnabled</c>
    /// died.</para>
    /// </remarks>
    [JsonPropertyName("weight")]
    public int Weight { get; init; } = 1;

    /// <summary>
    /// The declared key this element is bound to, or <see langword="null"/> for elements that do
    /// not read a key (<see cref="SurfaceButtonElement"/>).
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>
    /// The name this element's value travels under when an event is sent, or null when the element
    /// does not contribute a value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="Key"/>, and both are needed: <c>key</c> says which stored value to
    /// display, <c>name</c> says what to call the user's input when uploading it. They are usually
    /// the same word and must not be assumed to be — the input box may be filled from one key and
    /// the result filed under another.
    /// </para>
    /// <para>
    /// This is not the retired element <c>id</c> coming back. An id answers "which element is
    /// this", and nothing addresses elements. A name answers "what is this value called in the
    /// payload", which the server needs in order to project values onto <c>TriggerConfig.Fields</c>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>Read-only display of one key's value.</summary>
public sealed record SurfaceTextElement : SurfaceElement
{
    /// <summary>
    /// Optional formatting hint: <c>text</c>, <c>date</c>, <c>datetime</c> or <c>percent</c>.
    /// </summary>
    /// <remarks>
    /// A hint rather than a format string. The value arrives as JSON and the renderer knows its
    /// locale; a format string baked into the surface would make the card's appearance depend on
    /// who edited it rather than on who is looking.
    /// </remarks>
    [JsonPropertyName("format")]
    public string? Format { get; init; }
}

/// <summary>A command. Sends the listed events; reads no key.</summary>
public sealed record SurfaceButtonElement : SurfaceElement
{
    /// <summary>The button's text. Required — a button with no label is unclickable in practice.</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// The event names this button can send, matched against <c>TriggerConfig.Event</c>.
    /// </summary>
    /// <remarks>
    /// A list, not a single name: one button can legitimately wake more than one binding, and the
    /// dispatch side already matches per-trigger.
    /// </remarks>
    [JsonPropertyName("events")]
    public IReadOnlyList<string> Events { get; init; } = [];

    /// <summary>
    /// How much weight this button carries next to the others on the same card.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One of <c>primary</c> / <c>normal</c> / <c>danger</c>. Absent means <c>normal</c>.
    /// </para>
    /// <para>
    /// A string rather than an enum, for the same reason as <c>KeyDeclaration.Type</c>: a file that
    /// carries a value this build has never heard of should get a warning the author can act on,
    /// not a deserialisation failure that drops the whole card.
    /// </para>
    /// <para>
    /// <b>This field never affects layout.</b> The three values differ in background, foreground and
    /// border only — same height, same margins, same slot. A card that reflowed when a button became
    /// primary would make <c>row</c> stop being the only thing that decides position.
    /// </para>
    /// <para>
    /// <c>danger</c> marks a destructive action, and the renderer confirms it before sending: a
    /// confirmation the surface cannot configure, because a guard that can be turned off is not a
    /// guard. It is a <i>visual</i> marking, not a functional change — the button is still focusable
    /// and still in the tab order.
    /// </para>
    /// <para>
    /// <c>quiet</c> used to be a fourth value and was withdrawn: a button that looks like plain text
    /// is not found when it is wanted and is hit by accident when it is not. Destructive actions need
    /// marking, not hiding.
    /// </para>
    /// </remarks>
    [JsonPropertyName("emphasis")]
    public string? Emphasis { get; init; }
}

/// <summary>Free-text entry for one key.</summary>
public sealed record SurfaceInputElement : SurfaceElement
{
    /// <summary>
    /// The events this input can raise on its own, matched against <c>TriggerConfig.Event</c>.
    /// </summary>
    /// <remarks>
    /// An input that only ever feeds a button's event does not need this. It exists for the
    /// <c>Submit</c>-on-Enter case, where the control itself is what fires — the same shape
    /// <see cref="SurfaceButtonElement"/> has, because the question ("what can this thing send?")
    /// is the same.
    /// </remarks>
    [JsonPropertyName("events")]
    public IReadOnlyList<string> Events { get; init; } = [];
}

/// <summary>Numeric entry for one key.</summary>
public sealed record SurfaceNumberElement : SurfaceElement
{
    /// <summary>Lower bound, optional.</summary>
    [JsonPropertyName("min")]
    public double? Min { get; init; }

    /// <summary>Upper bound, optional.</summary>
    [JsonPropertyName("max")]
    public double? Max { get; init; }

    /// <summary>Increment for stepped input, optional.</summary>
    [JsonPropertyName("step")]
    public double? Step { get; init; }

    /// <summary>Digits to show after the decimal point, optional.</summary>
    [JsonPropertyName("decimals")]
    public int? Decimals { get; init; }
}

/// <summary>Choice from a fixed or key-derived set of options.</summary>
/// <remarks>
/// Both option sources are legal and mean different things. Static options are layout data —
/// changing a dropdown's candidates should not be a data-plane write. A key-derived list is
/// runtime data — a session picker's entries only exist while the workflow is running. Forcing one
/// into the other gets the ownership wrong in both directions.
/// </remarks>
public sealed record SurfaceSelectElement : SurfaceElement
{
    /// <summary>Options written into the surface itself.</summary>
    [JsonPropertyName("options")]
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>A declared key whose array value supplies the options. Mutually useful with <see cref="Options"/>.</summary>
    [JsonPropertyName("optionsKey")]
    public string? OptionsKey { get; init; }
}

/// <summary>Boolean toggle for one key.</summary>
public sealed record SurfaceSwitchElement : SurfaceElement;

/// <summary>Renders an array-valued key, one row per entry.</summary>
public sealed record SurfaceListElement : SurfaceElement
{
    /// <summary>Which property of each item to show as the primary line.</summary>
    /// <remarks>
    /// Optional, because a scalar array has no properties: <c>["one", "two"]</c> renders the whole
    /// value as the primary line. When the array holds objects this names the property to show;
    /// absent, the renderer falls back to the object's first string property rather than dropping
    /// the element.
    /// </remarks>
    [JsonPropertyName("itemTitle")]
    public string? ItemTitle { get; init; }

    /// <summary>Which property to show as the secondary line, optional.</summary>
    [JsonPropertyName("itemSubtitle")]
    public string? ItemSubtitle { get; init; }
}
