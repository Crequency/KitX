using System;

namespace KitX.ToolKit.Contract.Surface;

/// <summary>
/// The word a surface file uses for each element kind.
/// </summary>
/// <remarks>
/// <para><b>One vocabulary, three readers.</b> The same word appears in the file's <c>type</c>
/// discriminator (the <c>[JsonDerivedType]</c> names on <see cref="SurfaceElement"/>), in the
/// editor's cell corner label, and in every diagnostic that has to say which element it is about.
/// Each of those used to spell the list out for itself, and two of them spelled it with the C#
/// type name instead — so the same element was <c>Button</c> on the canvas and
/// <c>SurfaceButtonElement</c> in the sentence beside it.</para>
///
/// <para><b>A message that names a type names the wrong thing.</b> The reader of a diagnostic is
/// the person who wrote the card, and what they wrote is <c>"type": "Button"</c>. There is no
/// <c>SurfaceButtonElement</c> anywhere they can look — not in the file, not on the canvas, not in
/// the documentation.</para>
/// </remarks>
public static class SurfaceElementKind
{
    /// <summary>A keyed display of a stored scalar.</summary>
    public const string Text = "Text";

    /// <summary>The one kind whose gesture is speaking rather than holding a value.</summary>
    public const string Button = "Button";

    /// <summary>A single line the person types; its Enter is the gesture that sends.</summary>
    public const string Input = "Input";

    /// <summary>A number the person picks; changing it is the only gesture it has.</summary>
    public const string Number = "Number";

    /// <summary>A choice from a fixed or keyed list.</summary>
    public const string Select = "Select";

    /// <summary>A boolean the person ticks.</summary>
    public const string Switch = "Switch";

    /// <summary>A keyed display of a stored array.</summary>
    public const string List = "List";

    /// <summary>
    /// What a kind this build does not know is called. A word rather than an empty string, because
    /// every message that asks for a kind is a sentence, and a sentence with a hole in it reads as
    /// a bug in the sentence.
    /// </summary>
    public const string Unknown = "Element";

    /// <summary>
    /// The resource-key prefix a kind's localised name lives under: <c>Text_Bench_Surface_Kind_</c>.
    /// </summary>
    /// <remarks>
    /// Beside the vocabulary it names rather than in the client that reads it, because three places
    /// ask for the key — the diagnostics formatter, the editor's palette and the cell label — and a
    /// prefix written out three times is three chances to disagree about the underscore.
    /// </remarks>
    public const string NameKeyPrefix = "Text_Bench_Surface_Kind_";

    /// <summary>
    /// The resource key for the localised name of a kind, e.g.
    /// <c>Text_Bench_Surface_Kind_button</c>.
    /// </summary>
    /// <param name="kind">A file word: <see cref="Text"/>, <see cref="Button"/>, and so on.</param>
    /// <remarks>
    /// Lowercased because the resource dictionaries are keyed that way; a kind this build does not
    /// know still gets a well-formed key, which simply misses. Resolving the key is the client's
    /// job — this assembly has no dictionary — and so is falling back to the word itself.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    public static string NameKeyOf(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        return NameKeyPrefix + kind.ToLowerInvariant();
    }

    /// <summary>The resource key for the localised name of <paramref name="element"/>'s kind.</summary>
    /// <param name="element">The element to name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static string NameKeyOf(SurfaceElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return NameKeyOf(Of(element));
    }

    /// <summary>The file word for an element, or <see cref="Unknown"/> when there is none.</summary>
    /// <param name="element">The element to name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static string Of(SurfaceElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element switch
        {
            SurfaceTextElement => Text,
            SurfaceButtonElement => Button,
            SurfaceInputElement => Input,
            SurfaceNumberElement => Number,
            SurfaceSelectElement => Select,
            SurfaceSwitchElement => Switch,
            SurfaceListElement => List,
            _ => Unknown,
        };
    }
}
