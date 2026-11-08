using System.Text.Json.Serialization;

namespace KitX.ToolKit.Contract.Surface;

/// <summary>
/// One card on a ToolKit's surface: a 4-column grid of elements that read and write declared keys.
/// </summary>
/// <remarks>
/// <para><b>A surface is a view of keys, nothing more.</b> Every element either displays one
/// declared key or offers a way to write one. Elements carry no id — position is the identity
/// (Q80), so moving an element is an edit, not a delete-and-add. That also means the grid can be
/// sent as a whole and diffed by position.</para>
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
    /// How many columns the grid has.
    /// </summary>
    /// <remarks>
    /// <para>Fixed at 4 and not authorable: a phone in portrait is roughly 360–430 dp wide, so four
    /// columns are about 90–107 dp each — enough for one input or two buttons side by side. Making
    /// it tunable invites layouts that only work on one screen width.</para>
    ///
    /// <para><b>It is also a version probe.</b> A build that writes six columns would put
    /// <c>6</c> here, and an older renderer must refuse the file rather than reflow it into four —
    /// a reflowed card looks wrong in a way nobody can trace back to a version mismatch. So a value
    /// other than 4 is a load failure, not a hint to adapt.</para>
    /// </remarks>
    [JsonPropertyName("columns")]
    public int Columns { get; init; } = DefaultColumns;

    /// <summary>The only column count this build can lay out. See <see cref="Columns"/>.</summary>
    public const int DefaultColumns = 4;

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

    /// <summary>Which column the element starts at, 1-based, between 1 and the document's columns.</summary>
    [JsonPropertyName("column")]
    public int Column { get; init; } = 1;

    /// <summary>How many columns it spans. Defaults to 1; there is no row span.</summary>
    [JsonPropertyName("columnSpan")]
    public int ColumnSpan { get; init; } = 1;

    /// <summary>
    /// The declared key this element is bound to, or <see langword="null"/> for elements that do
    /// not read a key (<see cref="SurfaceButtonElement"/>).
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }
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
}

/// <summary>Free-text entry for one key.</summary>
public sealed record SurfaceInputElement : SurfaceElement;

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
