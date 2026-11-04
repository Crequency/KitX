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
/// </summary>
/// <param name="Severity">How bad this is.</param>
/// <param name="Code">Stable dotted identifier. Never localized, never reformatted.</param>
/// <param name="Message">Human-readable description, already localized by the producer.</param>
/// <param name="Path">Where it happened, when the producer can prove it.</param>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    DiagnosticPath? Path = null)
{
    /// <summary>Creates an <see cref="DiagnosticSeverity.Error"/>.</summary>
    public static Diagnostic Error(string code, string message, DiagnosticPath? path = null)
        => new(DiagnosticSeverity.Error, code, message, path);

    /// <summary>Creates a <see cref="DiagnosticSeverity.Warning"/>.</summary>
    public static Diagnostic Warning(string code, string message, DiagnosticPath? path = null)
        => new(DiagnosticSeverity.Warning, code, message, path);

    /// <summary>True when this diagnostic blocks the operation.</summary>
    public bool IsError => Severity == DiagnosticSeverity.Error;
}
