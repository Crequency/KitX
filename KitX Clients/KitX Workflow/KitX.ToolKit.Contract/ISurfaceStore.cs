using KitX.ToolKit.Contract.Surface;

namespace KitX.ToolKit.Contract;

/// <summary>
/// Reads and writes a ToolKit's card layout (<c>surface.json</c>), which lives next to
/// <c>toolkit.json</c> in the ToolKit's directory.
/// </summary>
/// <remarks>
/// <para><b>A narrower interface than <see cref="IToolkitService"/> on purpose.</b> The surface
/// editor needs to read and write exactly one file and hear about changes to it. Routing that
/// through the lifecycle facade would mean the editor could also mount, disable or delete — and
/// the editor is the one component that must never do those while a ToolKit is running
/// (ADR-0016 D3).</para>
///
/// <para><b>Editing the layout must not require unmounting.</b> That is the reason this file is
/// separate from <c>toolkit.json</c> in the first place: layout does not affect any running flow,
/// while keys and triggers do. Everything here is therefore outside the compiled artifact — the
/// compiler never reads this file (ADR-0016 D2).</para>
///
/// <para><b>No file watching.</b> <see cref="Changed"/> fires when <em>this</em> store writes, not
/// when something else edits the file on disk. Adding a watcher would be an admission that writers
/// bypass the store; the correct answer to that is to keep the writer count at one.</para>
/// </remarks>
public interface ISurfaceStore
{
    /// <summary>
    /// Loads a ToolKit's layout, reporting what could not be used.
    /// </summary>
    /// <remarks>
    /// <para>Never throws for a missing, unreadable or unrecognised file. A missing file is not a
    /// defect at all — it comes back as <see cref="SurfaceLoadResult.Empty"/> with both lists
    /// empty. The other outcomes are described on <see cref="SurfaceLoadResult"/>.</para>
    ///
    /// <para>It returns a result rather than a bare document because "the layout is empty" and
    /// "the layout could not be read" render identically but are consequences an author needs to
    /// tell apart: the first is a ToolKit without a card, the second is a file to fix.</para>
    /// </remarks>
    SurfaceLoadResult Load(string toolkitId);

    /// <summary>Writes a ToolKit's layout and raises <see cref="Changed"/>.</summary>
    void Save(string toolkitId, SurfaceDocument document);

    /// <summary>Removes a ToolKit's layout file, if any. Idempotent.</summary>
    void Delete(string toolkitId);

    /// <summary>
    /// Raised after <see cref="Save"/> or <see cref="Delete"/> changed a ToolKit's layout.
    /// </summary>
    /// <remarks>
    /// Carries the Toolkit id rather than the document: a subscriber that wants the content should
    /// call <see cref="Load"/>, so there is one representation of "what is on disk" rather than a
    /// pushed copy that can be stale by the time it is read.
    /// </remarks>
    event EventHandler<SurfaceChangedEventArgs>? Changed;
}

/// <summary>Which ToolKit's layout changed.</summary>
public sealed class SurfaceChangedEventArgs : EventArgs
{
    /// <summary>Creates the event payload.</summary>
    public SurfaceChangedEventArgs(string toolkitId) => ToolkitId = toolkitId;

    /// <summary>The ToolKit whose layout changed.</summary>
    public string ToolkitId { get; }
}
