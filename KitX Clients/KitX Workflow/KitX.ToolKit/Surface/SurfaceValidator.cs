using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
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
            var placed = ValidatePosition(declared, keys, ref warnings);
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

                    // A button is the one kind whose whole purpose is to speak, so a click that
                    // raises nothing is a dead control. Kept and reported rather than dropped: a card
                    // is saved mid-edit, and taking the button away would delete the author's work
                    // to tell them about it.
                    if (button.Events.Count == 0)
                    {
                        Warn(ref warnings, keys, button, SurfaceDiagnosticCodes.ButtonNoEvents,
                            "it declares no 'events', so clicking it sends nothing");
                    }

                    if (ValidateEmphasis(button, keys, ref warnings) is { } keptButton)
                        kept.Add(keptButton);
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

        // A card whose named values have nothing that can speak can never send them: a value travels
        // with an event, and events come from a button or from an input whose Enter is its own gesture.
        // Warned, never pruned: an author saves mid-edit, and a card caught halfway through being built
        // is the ordinary shape of one that is not finished yet.
        var contributes = kept.FirstOrDefault(e =>
            (e is SurfaceInputElement or SurfaceNumberElement or SurfaceSelectElement or SurfaceSwitchElement)
            && !string.IsNullOrWhiteSpace(e.Name));

        var speaks = kept.Any(e => e switch
        {
            SurfaceButtonElement button => button.Events.Count > 0,
            SurfaceInputElement input => input.Events.Count > 0,
            _ => false,
        });

        if (contributes is not null && !speaks)
        {
            (warnings ??= []).Add(Diagnostic.Warning(
                SurfaceDiagnosticCodes.ValuesHaveNoWayOut,
                $"'{contributes.Name}' and the values beside it have nothing that sends them: only a "
                + "Button, or an Input with 'events', raises an event. They stay on the card.",
                new DiagnosticPath(ToolkitId: keys.ToolkitId),
                Values(("name", contributes.Name!))));
        }

        // A document-level question, asked once after every element has had its say: how many main
        // actions does this card claim? One is a design; two means nobody decided.
        var primaries = kept
            .OfType<SurfaceButtonElement>()
            .Count(b => string.Equals(b.Emphasis, "primary", StringComparison.Ordinal));

        if (primaries > 1)
        {
            Warn(ref warnings, keys, kept[0], SurfaceDiagnosticCodes.ButtonRepeatedPrimary,
                $"{primaries} buttons claim 'primary'; a card reads best with at most one main action",
                data: [("count", Invariant(primaries))]);
        }

        if (warnings is null)
            return new SurfaceValidationResult(document, []);

        return new SurfaceValidationResult(document with { Elements = kept }, warnings);
    }

    /// <summary>The three emphasis values this build understands, in increasing weight order.</summary>
    private static readonly string[] KnownEmphasis = ["primary", "normal", "danger"];

    /// <summary>
    /// Checks a button's emphasis and, separately, whether the card ends up with more than one
    /// primary button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two different questions, so two different codes: "this value means nothing to me" is about one
    /// element, while "the card has two main actions" is about the whole document. Folding them into
    /// one warning would leave the reader unable to tell which one to fix.
    /// </para>
    /// <para>
    /// Neither drops or rewrites the element. An unknown emphasis is drawn as <c>normal</c> at render
    /// time; the validator only records that it did not understand the value. Two primaries are still
    /// two primaries — the card is drawable, it just has no single obvious first action.
    /// </para>
    /// </remarks>
    /// <summary>
    /// <c>emphasis</c> is one of three words, and anything else is drawn as <c>normal</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>The word is cleared, not just reported.</b> Returning the button with <c>prominent</c>
    /// still on it would leave every reader of the document — the renderer, the next validator, a
    /// test — with a value no specification defines, and the warning would be the only place the
    /// decision lived. Clearing it makes the effective document say what is actually drawn.</para>
    ///
    /// <para>Cleared to <see langword="null"/> rather than to <c>"normal"</c>: an author who wrote
    /// nothing and an author who wrote a word nobody knows both land on the same appearance, and the
    /// difference between them is exactly whether a warning was raised. Storing <c>"normal"</c> here
    /// would erase that difference for anyone reading the effective document.</para>
    /// </remarks>
    private static SurfaceButtonElement? ValidateEmphasis(
        SurfaceButtonElement button, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        if (button.Emphasis is null)
            return button;

        if (!KnownEmphasis.Contains(button.Emphasis, StringComparer.Ordinal))
        {
            // The English sentence marks its own literals with backticks, because the same sentence
            // is what the client falls back to when a code has no translation: an author reading it
            // has to be able to tell a field name from a word of prose. `known` carries the words
            // already wrapped, so a translation that says "不在 {known} 之中" gets the marks too.
            var known = string.Join(" / ", KnownEmphasis.Select(word => $"`{word}`"));

            Warn(ref warnings, keys, button, SurfaceDiagnosticCodes.ButtonUnknownEmphasis,
                $"`emphasis` is `{button.Emphasis}`, which is not one of " +
                $"{known}; it is drawn as `normal`",
                data: [("value", button.Emphasis), ("known", known)]);

            return button with { Emphasis = null };
        }

        return button;
    }

    /// <summary>
    /// The element must be in a real row and carry a usable weight. A row below 1 is undrawable and
    /// returns <see langword="null"/>; a bad weight is drawable (it means "one part"), so it is
    /// repaired to 1 and the element comes back adjusted.
    /// </summary>
    /// <remarks>
    /// There is no column to validate any more: horizontal placement is the row's own division by
    /// weight, so the only horizontal thing a single element can get wrong is a weight that is not a
    /// positive integer. The two clamping rules this replaces (column out of range, span too wide)
    /// had no counterpart here — there is nothing to run out of.
    /// </remarks>
    private static SurfaceElement? ValidatePosition(
        SurfaceElement element, KeyTable keys, ref List<Diagnostic>? warnings)
    {
        if (element.Row < 1)
        {
            Drop(ref warnings, keys, element, SurfaceDiagnosticCodes.PositionOutOfGrid,
                $"'row' is {element.Row}, which is not a row");
            return null;
        }

        if (element.Weight < 1)
        {
            Warn(ref warnings, keys, element, SurfaceDiagnosticCodes.WeightRepaired,
                $"'weight' is {element.Weight}; it is treated as 1. A weight is a positive number " +
                "of parts, and a row divides its width by the sum of the parts in it.");
            return element with { Weight = 1 };
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
                $"'format' is '{text.Format}', which this build does not know; it is shown as plain text",
                data: [("value", text.Format!)]);
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
                $"the ToolKit does not declare a key named '{element.Key}'",
                data: [("key", element.Key!)]);
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
                $"an '{SurfaceElementKind.Of(element)}' shows what the person typed, not a stored value; "
                + $"key '{element.Key}' is ignored — use 'name' to say what it uploads as",
                data: [("key", element.Key!)]);
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
                $"key '{list.Key}' is declared as '{declaration.Type}', not '{ArrayTypeName}'",
                data: [("key", list.Key!), ("type", declaration.Type)]);
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
                $"'min' ({min}) is greater than 'max' ({max}); the range is dropped",
                data: [("min", Invariant(min)), ("max", Invariant(max))]);
            result = result with { Min = null, Max = null };
        }

        if (result.Step is { } step && step <= 0)
        {
            Warn(ref warnings, keys, number, SurfaceDiagnosticCodes.NumberStepNotPositive,
                $"'step' ({step}) is not positive; it defaults to 1",
                data: [("step", Invariant(step))]);
            result = result with { Step = null };
        }

        if (result.Decimals is { } decimals && decimals < 0)
        {
            Warn(ref warnings, keys, number, SurfaceDiagnosticCodes.NumberDecimalsNegative,
                $"'decimals' ({decimals}) is negative; it defaults to the renderer's choice",
                data: [("decimals", Invariant(decimals))]);
            result = result with { Decimals = null };
        }

        return result;
    }

    /// <summary>
    /// One warning per element, named by kind and cell so a reader can find it in the editor
    /// without ids.
    /// </summary>
    /// <param name="data">
    /// The values this particular sentence names, one per <c>{name}</c> placeholder. <c>kind</c>,
    /// <c>row</c> and <c>weight</c> are added by <see cref="Warn"/> itself and must not be repeated
    /// here — a call site that supplied its own would be editing the sentence's opening clause,
    /// which it does not own.
    /// </param>
    private static void Drop(
        ref List<Diagnostic>? warnings,
        KeyTable keys,
        SurfaceElement element,
        string code,
        string detail,
        params (string Name, string Value)[] data)
        => Warn(ref warnings, keys, element, code, detail, dropped: true, data: data);

    private static void Warn(
        ref List<Diagnostic>? warnings,
        KeyTable keys,
        SurfaceElement element,
        string code,
        string detail,
        bool dropped = false,
        params (string Name, string Value)[] data)
    {
        var verb = dropped ? "Dropped" : "Kept";
        var kind = SurfaceElementKind.Of(element);

        // Every element diagnostic opens with "Dropped <kind> at row <row>, weight <weight>:" — the
        // three values a reader needs to find the cell, and the three no individual call site should
        // have to remember. Collected here once, so a translation of any of these codes may name
        // them without every producer having to pass them. The numbers are formatted invariantly
        // because they go into a translated sentence, where a group separator from the machine's
        // culture would be a foreign character.
        //
        // Two names for the same kind, and the difference is load-bearing. `kind` is the word in the
        // file — "type": "Input" — which is what the author can search for in surface.json, so it is
        // never translated. `kindName` is what this client should call it instead, and this assembly
        // has no dictionary: it records the file word as the fallback, and the client overwrites the
        // value with the localised name when it has one (see SurfaceDiagnosticText.Describe). A
        // sentence that printed only the file word read half in English on a Chinese screen; one
        // that printed only the localised name would leave the author nothing to search for.
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["kindName"] = kind,
            ["row"] = Invariant(element.Row),
            ["weight"] = Invariant(element.Weight),
        };

        foreach (var (name, value) in data)
            values[name] = value;

        (warnings ??= []).Add(Diagnostic.Warning(
            code,
            $"{verb} {kind} at row {element.Row}, weight {element.Weight}: {detail}.",
            new DiagnosticPath(ToolkitId: keys.ToolkitId, Key: element.Key),
            values));
    }

    /// <summary>
    /// The extra named values one diagnostic names, on top of the kind/row/weight every element
    /// diagnostic carries.
    /// </summary>
    /// <remarks>
    /// Adjacent to the message it belongs to, because nothing can check an interpolated string
    /// against a dictionary: the only force keeping <c>{key}</c> in the sentence and <c>key</c> in
    /// the data together is that they are written on the same call. <see langword="null"/> when a
    /// sentence reads on its own, so "nothing to substitute" has one representation.
    /// </remarks>
    private static IReadOnlyDictionary<string, string>? Values(params (string Name, string Value)[] pairs)
    {
        if (pairs.Length == 0)
            return null;

        var values = new Dictionary<string, string>(pairs.Length, StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
            values[name] = value;

        return values;
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(double value) => value.ToString(CultureInfo.InvariantCulture);

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
    /// <summary>
    /// The file did not declare a version field. Absent is not "the current one": a file that does
    /// not say which model it is written in cannot be read as if it said the current one.
    /// </summary>
    public const string FileMissingVersion = "surface.file.missing-version";

    /// <summary>
    /// The file carried a top-level field no property claims. The document still loads; the warning
    /// names the field so the author knows it went nowhere.
    /// </summary>
    public const string FileUnknownField = "surface.file.unknown-field";

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

    /// <summary>
    /// A kind that cannot speak declared <c>events</c>. The element is kept and the field ignored.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ElementUnknownField"/> on purpose. A misspelled field sends the author
    /// looking for the right spelling; this one has no right spelling — only a <c>Button</c> and an
    /// <c>Input</c> speak on their own, and the value of a <c>Number</c>, a <c>Select</c> or a
    /// <c>Switch</c> travels under whichever element does. One message for both would tell half the
    /// authors to fix a typo that does not exist.
    /// </remarks>
    public const string EventsNotAllowed = "surface.element.events-not-allowed";

    /// <summary>A button had no label.</summary>
    public const string ElementEmptyLabel = "surface.element.empty-label";

    /// <summary>A button declared no events, so clicking it sends nothing. The button is kept.</summary>
    /// <remarks>
    /// A warning rather than a drop, for the same reason the editor never blocks a save: a card is
    /// caught mid-edit, and the button is usually the thing the author was about to wire up. Removing
    /// it would delete their work in order to describe it.
    /// </remarks>
    public const string ButtonNoEvents = "surface.element.button.no-events";

    /// <summary>
    /// The card carries named values but nothing that raises an event, so none of them can ever be
    /// sent. Nothing is removed.
    /// </summary>
    /// <remarks>
    /// The same fact as <see cref="ElementInert"/>, one level up: that one is "this cell does nothing",
    /// this one is "these cells together do nothing". Reported and left alone, because a half-built
    /// card is the normal state of a card being edited.
    /// </remarks>
    public const string ValuesHaveNoWayOut = "surface.card.values-have-no-way-out";

    /// <summary>
    /// A button named an emphasis this build does not know. The element is kept and drawn as
    /// <c>normal</c>.
    /// </summary>
    /// <remarks>
    /// A warning rather than an error, for the same reason as <see cref="ElementUnknownField"/>: the
    /// reader who can act on it is the author, and the card is still drawable without that detail.
    /// </remarks>
    public const string ButtonUnknownEmphasis = "surface.element.button.unknown-emphasis";

    /// <summary>
    /// More than one button on the card claims <c>primary</c>. The card is still drawable — it just
    /// has no single obvious first action.
    /// </summary>
    /// <remarks>
    /// Deliberately a warning, not a rejection. "Which one is the main action" is a review comment on
    /// a card, not a reason to refuse to render it; refusing would make the author fix the card
    /// before they can even look at what they wrote.
    /// </remarks>
    public const string ButtonRepeatedPrimary = "surface.element.button.repeated-primary";

    /// <summary>A select had neither option source; it was dropped.</summary>
    public const string SelectNoOptions = "surface.element.select.no-options";

    /// <summary>A select had both option sources; the static one won.</summary>
    public const string SelectOptionsAmbiguous = "surface.element.select.ambiguous-options";

    /// <summary>A list's key was not declared as an array.</summary>
    public const string ListKeyNotArray = "surface.element.list.key-not-array";

    /// <summary>A row fell outside the document.</summary>
    public const string PositionOutOfGrid = "surface.element.position-out-of-grid";

    /// <summary>A weight was not a positive number of parts; it was treated as 1.</summary>
    public const string WeightRepaired = "surface.element.weight-repaired";

    /// <summary>A text element named a <c>format</c> this build does not know.</summary>
    public const string TextUnknownFormat = "surface.element.text.unknown-format";

    /// <summary>A number's range was reversed; both bounds were dropped.</summary>
    public const string NumberRangeReversed = "surface.element.number.range-reversed";

    /// <summary>A number's step was not positive; it was dropped.</summary>
    public const string NumberStepNotPositive = "surface.element.number.step-not-positive";

    /// <summary>A number's decimals count was negative; it was dropped.</summary>
    public const string NumberDecimalsNegative = "surface.element.number.decimals-negative";
}
