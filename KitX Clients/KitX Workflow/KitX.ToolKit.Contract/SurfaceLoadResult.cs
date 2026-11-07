using KitX.ToolKit.Contract.Surface;

namespace KitX.ToolKit.Contract;

/// <summary>
/// Reading <c>surface.json</c> produces a document plus an account of what went wrong doing it.
/// </summary>
/// <remarks>
/// <para><b>Two lists, because the caller can act on them differently.</b> <see cref="Errors"/> means
/// the file as a whole could not be trusted and <see cref="Document"/> is empty; the honest response
/// is "show no card and tell the author". <see cref="Warnings"/> means the document loaded but some
/// elements were dropped; the honest response is "show the card, minus those cells".</para>
///
/// <para><b>The line is "can a renderer draw anything", not severity.</b> A file that declares rules
/// this build cannot honour must not be half-interpreted — that is how a six-column layout gets
/// reflowed into four and silently looks wrong. A file with one element of an unknown kind still has
/// a meaningful remainder.</para>
///
/// <para><b>An absent file is neither.</b> A ToolKit nobody has laid out yet is not an error
/// (ADR-0016 D4), so it comes back with an empty document and nothing in either list. An empty
/// document with a warning means the opposite: someone drew a card and left it blank.</para>
/// </remarks>
public sealed record SurfaceLoadResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="Document">The document to render, or <see cref="SurfaceDocument.Empty"/>.</param>
    /// <param name="Errors">Non-empty when the whole file was refused.</param>
    /// <param name="Warnings">One entry per element that was dropped while the rest loaded.</param>
    public SurfaceLoadResult(
        SurfaceDocument Document,
        IReadOnlyList<Diagnostic> Errors,
        IReadOnlyList<Diagnostic> Warnings)
    {
        ArgumentNullException.ThrowIfNull(Document);
        ArgumentNullException.ThrowIfNull(Errors);
        ArgumentNullException.ThrowIfNull(Warnings);

        this.Document = Document;
        this.Errors = Errors;
        this.Warnings = Warnings;
    }

    /// <summary>The document to render. Empty whenever <see cref="Errors"/> is non-empty.</summary>
    public SurfaceDocument Document { get; }

    /// <summary>File-level problems. Non-empty means nothing could be trusted.</summary>
    public IReadOnlyList<Diagnostic> Errors { get; }

    /// <summary>Element-level problems. The document loaded without them.</summary>
    public IReadOnlyList<Diagnostic> Warnings { get; }

    /// <summary>No file, nothing wrong: a ToolKit whose author did not draw a card.</summary>
    public static SurfaceLoadResult Empty { get; } =
        new(SurfaceDocument.Empty, [], []);

    /// <summary>The whole file was refused.</summary>
    /// <param name="error">Why. The caller reports it and renders nothing.</param>
    public static SurfaceLoadResult Refused(Diagnostic error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new SurfaceLoadResult(SurfaceDocument.Empty, [error], []);
    }
}
