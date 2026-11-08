namespace KitX.ToolKit.Contract.Declaration;

/// <summary>
/// One name a trigger's event carries, with the type the workflow should expect.
/// </summary>
/// <remarks>
/// <para>
/// This is the contract half of Q91/Q92. The surface declares which inputs exist and what they are
/// called; the trigger declares which of those names its workflow is willing to receive. Without
/// this, an event's payload is a bag of whatever the client chose to send, and every top-level
/// scalar in it becomes a workflow constant — which lets a client overwrite a workflow's own
/// <c>const barcode</c> just by sending a property with that name.
/// </para>
/// <para>
/// A class with settable properties rather than a positional record, because this is config: it is
/// hand-edited and round-tripped through JSON, and a missing property must read as "not set"
/// rather than as a constructor argument someone forgot.
/// </para>
/// </remarks>
public sealed class TriggerField
{
    /// <summary>The name the value arrives under, e.g. <c>barcode</c>.</summary>
    /// <remarks>
    /// Matched against the surface element's <c>name</c> and against <c>$payload.&lt;name&gt;</c> in
    /// workflow bindings. Case-sensitive: these are identifiers, not display text.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What kind of value it is. One of <c>string</c>, <c>number</c>, <c>boolean</c>,
    /// <c>object</c>, <c>array</c>, <c>enum</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first five are the same literals <see cref="KeyDeclaration.Type"/> uses, deliberately.
    /// A second vocabulary for the same idea would let one surface describe the same thing two
    /// ways, and <see cref="Surface.SurfaceSelectElement.OptionsKey"/> pointing at an
    /// <c>array</c>-typed key would then need a translation table.
    /// </para>
    /// <para>
    /// A string rather than an enum for the same reason as <see cref="KeyDeclaration.Type"/>: a
    /// file naming a type this build has not heard of should produce a diagnostic, not an
    /// unhandled deserialization failure that takes the whole config down with it.
    /// </para>
    /// </remarks>
    public string Type { get; set; } = "string";

    /// <summary>The allowed values when <see cref="Type"/> is <c>enum</c>; ignored otherwise.</summary>
    /// <remarks>
    /// Static only. Candidates that follow a data key stay on
    /// <see cref="Surface.SurfaceSelectElement.OptionsKey"/> — one is layout data, the other is
    /// runtime data, and merging them would make a dropdown's contents depend on who wrote it.
    /// </remarks>
    public IReadOnlyList<string>? Options { get; set; }
}
