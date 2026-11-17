using System;
using System.IO;

namespace KitX.ToolKit.Contract;

/// <summary>
/// A card could not be written, and this says which file it was.
/// </summary>
/// <remarks>
/// <para><b>The path the platform names is not the path the author can look at.</b> The atomic write
/// composes the new content in <c>surface.json.tmp</c> and then swaps it into place, so a failure at
/// the swap reports the temporary file's name — a file the author has never seen, that may not exist
/// any more by the time they read the message, and that says nothing about which card it belonged to.
/// The store knows the real path because it computed it; this exception carries it out.</para>
///
/// <para><b>Why not classify the cause.</b> An earlier draft branched on <see cref="Exception.HResult"/>
/// into "disk full", "no permission" and "no folder", with a sentence for each. Measured on this
/// machine, a read-only file throws <see cref="UnauthorizedAccessException"/> — one branch. The other
/// two could only be reached by exceptions built in a test, so two thirds of that vocabulary would
/// have been copy for situations nobody had observed. The author still gets the platform's own first
/// line, which names the actual trouble, and the path, which is the part the platform cannot say.</para>
/// </remarks>
public sealed class SurfaceWriteException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="toolkitId">The ToolKit whose card could not be written.</param>
    /// <param name="path">The card's own path — not the temporary file's.</param>
    /// <param name="inner">What the write threw.</param>
    public SurfaceWriteException(string toolkitId, string path, Exception inner)
        : base($"'{path}' could not be written: {inner.Message}", inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(inner);

        ToolkitId = toolkitId;
        Path = path;
    }

    /// <summary>The ToolKit whose card this is.</summary>
    public string ToolkitId { get; }

    /// <summary>The card's path, as the store computed it — never the temporary file's.</summary>
    public string Path { get; }
}
