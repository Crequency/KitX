namespace KitX.ToolKit.Contract;

/// <summary>
/// Severity of a <see cref="Diagnostic"/> (ADR-0007 D1).
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>The operation cannot proceed. Compilation with any Error yields no result.</summary>
    Error,

    /// <summary>The operation proceeds, but something is probably not what the author meant.</summary>
    Warning,

    /// <summary>Informational note, e.g. a deprecated field still being read.</summary>
    Info,
}
