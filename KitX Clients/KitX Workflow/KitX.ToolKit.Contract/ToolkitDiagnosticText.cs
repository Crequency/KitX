namespace KitX.ToolKit.Contract;

/// <summary>
/// Diagnostic codes and message shapes shared by more than one implementation.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The cycle message was written out twice — once by
/// <see cref="ConfigValidator"/> (the editor's keystroke-level check) and once by the compiler
/// (the mount-time gate) — and the editor's edge-rollback logic decides what to do by matching
/// that message text (<c>Errors.Any(e =&gt; e.Contains("cycle"))</c>). Two hand-synchronised
/// copies of a string that a caller pattern-matches is a silent-failure waiting to happen: change
/// one wording and the rollback simply stops happening, without an error.</para>
///
/// <para>Sharing the text here does not make the two checks agree — they still run over different
/// inputs and can legitimately disagree (see below). It only means the sentence they emit cannot
/// drift apart. Merging the implementations is a separate, larger job.</para>
/// </remarks>
public static class ToolkitDiagnosticText
{
    /// <summary>
    /// The cycle message. Both the editor check and the compile-time check emit exactly this.
    /// </summary>
    /// <param name="path">The cycle, already joined for display by the caller's own separator.</param>
    /// <remarks>
    /// Takes the joined path rather than the sequence so callers keep their own formatting. That
    /// is the whole point: the prefix is what gets matched, so the prefix is what gets shared.
    /// </remarks>
    public static string Cycle(string path) => $"WorkflowCompletion cycle detected: {path}.";
}
