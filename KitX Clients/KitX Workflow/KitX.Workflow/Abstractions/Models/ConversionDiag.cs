namespace KitX.Workflow.Abstractions.Models;

/// <summary>
/// Severity of a conversion/parse diagnostic. Mirrors the compile-path model so the
/// Dashboard editor output panel can render conversion problems the same way it renders
/// Roslyn compile errors.
/// </summary>
public enum ConversionDiagSeverity
{
    /// <summary>Blocks conversion from producing correct output; should surface to the user.</summary>
    Error,

    /// <summary>Non-fatal (e.g. dead code per BlockScript §6); surfaced as a warning.</summary>
    Warning,
}

/// <summary>
/// A single user-facing diagnostic produced during BlockScript parsing or BS↔BP conversion.
/// Stable <see cref="Code"/> lets the editor/UI filter and deduplicate. <see cref="LineNumber"/>
/// is 1-based, matching <see cref="BlockStatement.LineNumber"/>.
/// </summary>
public sealed record ConversionDiag(ConversionDiagSeverity Severity, string Code, string Message, int? LineNumber = null);