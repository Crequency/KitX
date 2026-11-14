namespace KitX.ToolKit.Contract;

/// <summary>
/// A structured, machine-readable problem report (ADR-0007 D1). Defined in the contract
/// assembly because three different consumers read the same instance: the Dashboard
/// (display + jump-to-node), AI tooling (self-correction), and the future network View
/// contract (<c>rejected{code,message}</c>).
///
/// <para><b><see cref="Code"/> MUST be a stable value</b> — e.g. <c>"toolkit.id.duplicate"</c>,
/// <c>"toolkit.spawn.max-instances"</c>. It MUST NOT be derived from the message text, from
/// a hash, or from a localized string. Clients branch on <see cref="Code"/>; only humans
/// read <see cref="Message"/>. Once a code ships it cannot change meaning, so the code
/// list is a compatibility surface.</para>
///
/// <para><b><see cref="Message"/> is English, not localized.</b> "Localized by the producer" was
/// the claim here, and it cannot hold: the producer runs in the server assembly
/// (<c>KitX.ToolKit</c>) and has no language context at all — the language files live in the
/// Dashboard. So the division of labour is: <see cref="Code"/> picks the sentence, <see cref="Data"/>
/// supplies the values in it, <see cref="Message"/> is the English fallback. A client that shows a
/// diagnostic in the user's language looks up <c>Text_Bench_Diag_&lt;code&gt;</c> (the
/// <c>surface.</c> prefix dropped, <c>.</c> and <c>-</c> replaced by <c>_</c>), substitutes the
/// <c>{name}</c> placeholders from <see cref="Data"/>, and falls back to <see cref="Message"/> when
/// nothing answers. Logs, AI self-correction and scripts read <see cref="Message"/> and never the
/// translation.</para>
/// </summary>
/// <param name="Severity">How bad this is.</param>
/// <param name="Code">Stable dotted identifier. Never localized, never reformatted.</param>
/// <param name="Message">
/// English, human-readable description. The fallback for a reader with no translation, and the text
/// every non-UI consumer reads. Never the only copy of a user-facing sentence — see <paramref name="Data"/>.
/// </param>
/// <param name="Path">Where it happened, when the producer can prove it.</param>
/// <param name="Data">
/// The named values this diagnostic's sentence is made of — <c>kind</c>, <c>row</c>, <c>key</c> and
/// so on, one entry per <c>{name}</c> placeholder. A translation may reorder them, which is why the
/// placeholders are named rather than positional. <see langword="null"/> when the sentence needs no
/// values; a missing name leaves its placeholder standing rather than blanking the sentence.
/// </param>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DiagnosticPath? Path = null,
    IReadOnlyDictionary<string, string>? Data = null)
{
    /// <summary>Creates an <see cref="DiagnosticSeverity.Error"/>.</summary>
    public static Diagnostic Error(
        string code,
        string message,
        DiagnosticPath? path = null,
        IReadOnlyDictionary<string, string>? data = null)
        => new(DiagnosticSeverity.Error, code, message, path, data);

    /// <summary>Creates a <see cref="DiagnosticSeverity.Warning"/>.</summary>
    public static Diagnostic Warning(
        string code,
        string message,
        DiagnosticPath? path = null,
        IReadOnlyDictionary<string, string>? data = null)
        => new(DiagnosticSeverity.Warning, code, message, path, data);

    /// <summary>True when this diagnostic blocks the operation.</summary>
    public bool IsError => Severity == DiagnosticSeverity.Error;
}
