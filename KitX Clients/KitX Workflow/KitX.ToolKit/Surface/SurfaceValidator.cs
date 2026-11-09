using System.Collections.Immutable;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Surface;
using KitX.ToolKit.Data;

namespace KitX.ToolKit.Surface;

/// <summary>
/// Checks a loaded surface against a ToolKit's declared keys and element rules.
/// </summary>
/// <remarks>
/// <para><b>Runs after compilation and takes the key table as an argument.</b> The compiler is the
/// authority on which keys exist, and compiling must not read the surface — otherwise retyping the
/// layout would require unmounting (ADR-0016 D2). Nothing in the compile path references this
/// file, and this file never reaches for a <see cref="KeyTable"/> of its own.</para>
///
/// <para><b>Never throws and never rejects the whole document.</b> A stale element is the expected
/// result of "delete a key, then fix the card" — an intermediate state an author passes through,
/// not a broken ToolKit. Dropping the cell and warning is proportionate; failing the load would
/// turn a typo into a blank screen. File-level refusal happens earlier, in the store.</para>
///
/// <para><b>Three outcomes, matching §3.3.4.</b> An element that cannot be drawn at all is dropped
/// (a cell with no key, a key that does not exist, a select with no options). An element that can
/// be drawn but has a bad field is kept with the field clamped or defaulted (a negative span, a
/// reversed range, an unknown format). Only the first kind disappears.</para>
/// </remarks>
internal static class SurfaceValidator
{
    /// <summary>Validates a document against a ToolKit's declared keys.</summary>
    /// <param name="document">
    /// The layout as loaded, or <see langword="null"/> when no file exists. A refused file never
    /// reaches here — the store turns that into an error before validation is reached.
    /// </param>
    /// <param name="keys">The ToolKit's compiled key table — the authority on what exists.</param>
    /// <returns>The elements that survive, with bad fields clamped, plus one warning per change.</returns>
    public static SurfaceValidationResult Validate(SurfaceDocument? document, KeyTable keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (document is null || document.Elements.Count == 0)
            return new SurfaceValidationResult(document ?? SurfaceDocument.Empty, []);

        var kept = new List<SurfaceElement>(document.Elements.Count);
        List<Diagnostic>? warnings = null;

        foreach (var declared in document.Elements)
        {
            // Position is checked first and for every kind: an element outside the grid cannot be
            // drawn at all, so nothing else about it matters. A span that merely overflows is
            // clamped instead — the cell still exists.
            var placed = ValidatePosition(declared, document.Columns, keys, ref warnings);
            if (placed is null)
                continue;

            var element = placed;

            switch (element)
            {
                case SurfaceButtonElement button:
                    // The one kind that reads no key. Its label is required, though: a button with
                    // no text is unclickable in practice, so an empty one is not drawable.
                    if (string.IsNullOrWhiteSpace(button.Label))
                    {
                        Drop(ref warnings, keys, element, SurfaceDiagnosticCodes.ElementEmptyLabel,
                            "it has no label, so there is nothing to click");
                        continue;
                    }

                    kept.Add(button);
                    break;

                case SurfaceSelectElement select:
                    if (ValidateSelect(select, keys, ref warnings) is { } keptSelect)
                        kept.Add(keptSelect);
                    break;

                case SurfaceListElement list:
                    if (!ValidateList(list, keys, ref warnings))
                        continue;
                    kept.Add(list);
                    break;

                case SurfaceNumberElement number:
                    if (ValidateNumber(number, keys, ref warnings) is { } keptNumber)
                        kept.Add(keptNumber);
                    break;

                case SurfaceTextElement text:
                    if (ValidateText(text, keys, ref warnings) is { } keptText)
                        kept.Add(keptText);
                    break;

                default:
                    // Input and Switch. They show what the person typed, so they carry no key.
                    if (ValidateEditing(element, keys, ref warnings) is { } keptInput)
                        kept.Add(keptInput);
                    break;
            }
        }

        if (warnings is null)
            return new SurfaceValidationResult(document, []);

        return new SurfaceValidationResult(document with { Elements = kept }, warnings);
    }

    /// <summary>
    /// The cell must exist and the span must fit. A cell outside the grid is undrawable and returns
    /// <see langword="null"/>; a span that overflows is still drawable, so it is clamped and the
    /// element comes back adjusted.
    /// </summary>
    private static SurfaceElement? ValidatePosition(
        SurfaceElement element, int columns, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        if (element.Row < 1)
        {
            Drop(ref warnings, keys, element, SurfaceDiagnosticCodes.PositionOutOfGrid,
                $"'row' is {element.Row}, which is not a row");
            return null;
        }

        if (element.Column < 1 || element.Column > columns)
        {
            Drop(ref warnings, keys, element, SurfaceDiagnosticCodes.PositionOutOfGrid,
                $"'column' is {element.Column}, outside 1..{columns}");
            return null;
        }

        var room = columns - element.Column + 1;

        if (element.ColumnSpan < 1)
        {
            Warn(ref warnings, keys, element, SurfaceDiagnosticCodes.SpanClamped,
                $"'columnSpan' is {element.ColumnSpan}; it is treated as 1");
            return element with { ColumnSpan = 1 };
        }

        if (element.ColumnSpan > room)
        {
            Warn(ref warnings, keys, element, SurfaceDiagnosticCodes.SpanClamped,
                $"'columnSpan' is {element.ColumnSpan} but only {room} column(s) remain from " +
                $"column {element.Column}; it is clamped to {room}");
            return element with { ColumnSpan = room };
        }

        return element;
    }

    /// <summary>
    /// An unknown <c>format</c> still draws as plain text, so the element is kept with the hint
    /// dropped rather than refused.
    /// </summary>
    /// <returns>The element to keep, or <see langword="null"/> when it cannot be drawn.</returns>
    private static SurfaceTextElement? ValidateText(
        SurfaceTextElement text, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        if (!ValidateKeyed(text, keys, ref warnings))
            return null;

        if (string.IsNullOrWhiteSpace(text.Format))
            return text;

        if (!KnownFormats.Contains(text.Format))
        {
            Warn(ref warnings, keys, text, SurfaceDiagnosticCodes.TextUnknownFormat,
                $"'format' is '{text.Format}', which this build does not know; it is shown as plain text");
            return text with { Format = null };
        }

        return text;
    }

    /// <summary>The <c>format</c> hints a text element may carry. See <see cref="SurfaceTextElement.Format"/>.</summary>
    private static readonly ImmutableHashSet<string> KnownFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "text", "date", "datetime", "percent");

    /// <summary>
    /// The check for the two kinds that show a stored value: the key must be named and declared.
    /// Returns false when the element cannot be drawn.
    /// </summary>
    /// <remarks>
    /// Only <c>Text</c> and <c>List</c> reach this. `key` answers "which stored value to display",
    /// so the four kinds that display something the person typed have nothing to point it at —
    /// see <see cref="ValidateEditing"/>.
    /// </remarks>
    private static bool ValidateKeyed(
        SurfaceElement element, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        if (string.IsNullOrWhiteSpace(element.Key))
        {
            Drop(ref warnings, keys, element, SurfaceDiagnosticCodes.ElementNoKey,
                "it is not bound to a key and no key name was given");
            return false;
        }

        if (!keys.IsDeclared(element.Key))
        {
            Drop(ref warnings, keys, element, SurfaceDiagnosticCodes.ElementUnknownKey,
                $"the ToolKit does not declare a key named '{element.Key}'");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The check for the four kinds that show something the person typed rather than something the
    /// ToolKit stored: an input, a number, a select and a switch.
    /// </summary>
    /// <remarks>
    /// Such an element carries no key. `key` says which stored value to display, and there is no
    /// stored value behind these — the value travels the other way, from the control to the server,
    /// under the element's <c>name</c>. An author who writes one anyway is not wrong about what they
    /// want; they are using the only field they could find, so the key is dropped with a warning
    /// rather than the element.
    ///
    /// What does make an element undrawable is having neither a name nor any events: it neither
    /// contributes a value to a payload nor speaks on its own, so it is not an input at all — it is
    /// an input-shaped decoration, and dropping it is the honest reading.
    /// </remarks>
    /// <returns>The element to keep, or <see langword="null"/> when it cannot be drawn.</returns>
    private static SurfaceElement? ValidateEditing(
        SurfaceElement element, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        var kept = element;

        if (!string.IsNullOrWhiteSpace(element.Key))
        {
            Warn(ref warnings, keys, element, SurfaceDiagnosticCodes.ElementKeyIgnored,
                $"an '{element.GetType().Name}' shows what the person typed, not a stored value; "
                + $"key '{element.Key}' is ignored — use 'name' to say what it uploads as");
            kept = element with { Key = null };
        }

        var named = !string.IsNullOrWhiteSpace(element.Name);
        var raises = element is SurfaceInputElement { Events.Count: > 0 }
            || element is SurfaceButtonElement;

        if (!named && !raises)
        {
            Drop(ref warnings, keys, element, SurfaceDiagnosticCodes.ElementInert,
                "it has no 'name' and no 'events', so it neither contributes a value nor sends one");
            return null;
        }

        return kept;
    }

    /// <summary>
    /// A select must have exactly one source of options. Both means the static list wins — it is
    /// what the author typed on this element — and neither means there is nothing to choose from.
    /// </summary>
    /// <returns>The element to keep, or <see langword="null"/> when it cannot be drawn.</returns>
    private static SurfaceSelectElement? ValidateSelect(
        SurfaceSelectElement select, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        if (ValidateEditing(select, keys, ref warnings) is not { } keptSelect)
            return null;

        select = keptSelect as SurfaceSelectElement
            ?? throw new InvalidOperationException("ValidateEditing returned a different element");
        var hasStatic = select.Options.Count > 0;
        var hasKey = !string.IsNullOrWhiteSpace(select.OptionsKey);

        if (hasStatic && hasKey)
        {
            // Keep the element; prefer what the author wrote here over an indirection.
            Warn(ref warnings, keys, select, SurfaceDiagnosticCodes.SelectOptionsAmbiguous,
                "it has both 'options' and 'optionsKey'; the static list wins");
            return select with { OptionsKey = null };
        }

        if (!hasStatic && !hasKey)
        {
            Drop(ref warnings, keys, select, SurfaceDiagnosticCodes.SelectNoOptions,
                "it has neither 'options' nor 'optionsKey', so there is nothing to choose from");
            return null;
        }

        return select;
    }

    /// <summary>
    /// A list renders an array-valued key. The declared type is advisory (the store does not
    /// enforce it), so this checks the declaration the author wrote, not the runtime value —
    /// a value that turns out not to be an array is a render-time degradation, not a load error.
    /// </summary>
    private static bool ValidateList(
        SurfaceListElement list, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        if (!ValidateKeyed(list, keys, ref warnings))
            return false;

        if (!keys.TryResolve(list.Key!, out var declaration))
            return true;

        if (!string.Equals(declaration.Type, ArrayTypeName, StringComparison.OrdinalIgnoreCase))
        {
            Drop(ref warnings, keys, list, SurfaceDiagnosticCodes.ListKeyNotArray,
                $"key '{list.Key}' is declared as '{declaration.Type}', not '{ArrayTypeName}'");
            return false;
        }

        return true;
    }
    /// <summary>
    /// A number with a reversed or empty range still draws; clamp rather than drop.
    /// </summary>
    /// <returns>The element to keep, or <see langword="null"/> when it cannot be drawn.</returns>
    private static SurfaceNumberElement? ValidateNumber(
        SurfaceNumberElement number, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        if (ValidateEditing(number, keys, ref warnings) is not { } keptNumber)
            return null;

        number = keptNumber as SurfaceNumberElement
            ?? throw new InvalidOperationException("ValidateEditing returned a different element");

        var result = number;

        if (number.Min is { } min && number.Max is { } max && min > max)
        {
            Warn(ref warnings, keys, number, SurfaceDiagnosticCodes.NumberRangeReversed,
                $"'min' ({min}) is greater than 'max' ({max}); the range is dropped");
            result = result with { Min = null, Max = null };
        }

        if (result.Step is { } step && step <= 0)
        {
            Warn(ref warnings, keys, number, SurfaceDiagnosticCodes.NumberStepNotPositive,
                $"'step' ({step}) is not positive; it defaults to 1");
            result = result with { Step = null };
        }

        if (result.Decimals is { } decimals && decimals < 0)
        {
            Warn(ref warnings, keys, number, SurfaceDiagnosticCodes.NumberDecimalsNegative,
                $"'decimals' ({decimals}) is negative; it defaults to the renderer's choice");
            result = result with { Decimals = null };
        }

        return result;
    }

    /// <summary>
    /// One warning per element, named by kind and cell so a reader can find it in the editor
    /// without ids.
    /// </summary>
    private static void Drop(
        ref List<Diagnostic>? warnings, KeyTable keys, SurfaceElement element, string code, string detail)
        => Warn(ref warnings, keys, element, code, detail, dropped: true);

    private static void Warn(
        ref List<Diagnostic>? warnings,
        KeyTable keys,
        SurfaceElement element,
        string code,
        string detail,
        bool dropped = false)
    {
        var verb = dropped ? "Dropped" : "Kept";
        (warnings ??= []).Add(Diagnostic.Warning(
            code,
            $"{verb} {element.GetType().Name} at row {element.Row}, column {element.Column}: {detail}.",
            new DiagnosticPath(ToolkitId: keys.ToolkitId, Key: element.Key)));
    }

    /// <summary>The key-type literal a <see cref="SurfaceListElement"/> requires.</summary>
    private const string ArrayTypeName = "array";
}

/// <summary>The outcome of validating a surface: what survives, and what was changed.</summary>
/// <param name="Effective">The document with offending elements removed and bad fields clamped.</param>
/// <param name="Warnings">One entry per dropped element or adjusted field; empty when nothing changed.</param>
internal sealed record SurfaceValidationResult(
    SurfaceDocument Effective,
    IReadOnlyList<Diagnostic> Warnings);

/// <summary>Stable codes the surface validator emits.</summary>
internal static class SurfaceDiagnosticCodes
{
    /// <summary>A non-button element named no key.</summary>
    public const string ElementNoKey = "surface.element.no-key";

    /// <summary>An element referenced a key the ToolKit does not declare.</summary>
    public const string ElementUnknownKey = "surface.element.unknown-key";

    /// <summary>
    /// An editing element declared a key. `key` says which stored value to display, and these four
    /// show what the person typed, so the key is dropped and the element kept.
    /// </summary>
    public const string ElementKeyIgnored = "surface.element.key-ignored";

    /// <summary>
    /// An editing element declared neither a name nor any events, so it neither contributes a value
    /// to a payload nor speaks on its own.
    /// </summary>
    public const string ElementInert = "surface.element.inert";

    /// <summary>A field name no property on the element type claims. The element is kept.</summary>
    public const string ElementUnknownField = "surface.element.unknown-field";

    /// <summary>A button had no label.</summary>
    public const string ElementEmptyLabel = "surface.element.empty-label";

    /// <summary>A select had neither option source; it was dropped.</summary>
    public const string SelectNoOptions = "surface.element.select.no-options";

    /// <summary>A select had both option sources; the static one won.</summary>
    public const string SelectOptionsAmbiguous = "surface.element.select.ambiguous-options";

    /// <summary>A list's key was not declared as an array.</summary>
    public const string ListKeyNotArray = "surface.element.list.key-not-array";

    /// <summary>A row or column fell outside the grid.</summary>
    public const string PositionOutOfGrid = "surface.element.position-out-of-grid";

    /// <summary>A span was not positive or did not fit; it was clamped.</summary>
    public const string SpanClamped = "surface.element.span-clamped";

    /// <summary>A text element named a <c>format</c> this build does not know.</summary>
    public const string TextUnknownFormat = "surface.element.text.unknown-format";

    /// <summary>A number's range was reversed; both bounds were dropped.</summary>
    public const string NumberRangeReversed = "surface.element.number.range-reversed";

    /// <summary>A number's step was not positive; it was dropped.</summary>
    public const string NumberStepNotPositive = "surface.element.number.step-not-positive";

    /// <summary>A number's decimals count was negative; it was dropped.</summary>
    public const string NumberDecimalsNegative = "surface.element.number.decimals-negative";
}
