using System.Collections.Generic;
using System.Linq;
using System.Text;

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

/// <summary>
/// Collects user-facing diagnostics across the parse + conversion pipeline, mirroring the
/// <c>out IReadOnlyList&lt;string&gt; errors</c> channel already used by the Roslyn compile path.
/// Backend-bug-class problems (unexpected null roots, registry instantiation failures, missing
/// nodes during assembly) are NOT collected here — those go to Serilog and stay in backend logs.
/// </summary>
public sealed class ConversionDiagnostics
{
    private readonly List<ConversionDiag> _items = new();

    /// <summary>All diagnostics in insertion order.</summary>
    public IReadOnlyList<ConversionDiag> Items => _items;

    /// <summary>True when at least one <see cref="ConversionDiagSeverity.Error"/> was recorded.</summary>
    public bool HasErrors => _items.Any(d => d.Severity == ConversionDiagSeverity.Error);

    /// <summary>True when at least one <see cref="ConversionDiagSeverity.Warning"/> was recorded.</summary>
    public bool HasWarnings => _items.Any(d => d.Severity == ConversionDiagSeverity.Warning);

    public IEnumerable<ConversionDiag> Errors => _items.Where(d => d.Severity == ConversionDiagSeverity.Error);
    public IEnumerable<ConversionDiag> Warnings => _items.Where(d => d.Severity == ConversionDiagSeverity.Warning);

    /// <summary>Records a diagnostic.</summary>
    public void Add(ConversionDiag diag) => _items.Add(diag);

    /// <summary>Convenience: record an error diagnostic.</summary>
    public void AddError(string code, string message, int? lineNumber = null)
        => _items.Add(new ConversionDiag(ConversionDiagSeverity.Error, code, message, lineNumber));

    /// <summary>Convenience: record a warning diagnostic.</summary>
    public void AddWarning(string code, string message, int? lineNumber = null)
        => _items.Add(new ConversionDiag(ConversionDiagSeverity.Warning, code, message, lineNumber));

    /// <summary>Appends all diagnostics from another collector (e.g. parse-time into convert-time).</summary>
    public void AddRange(ConversionDiagnostics? other)
    {
        if (other == null) return;
        _items.AddRange(other._items);
    }

    /// <summary>
    /// Renders the diagnostics as a single multi-line string suitable for the Dashboard editor
    /// output panel. Mirrors <c>BlockScriptExecutor.FormatCompileErrors</c>: capped at 10 entries
    /// so a cascade stays readable. Returns an empty string when there are no diagnostics.
    /// </summary>
    public string Format()
    {
        if (_items.Count == 0) return string.Empty;

        const int maxShown = 10;
        var sb = new StringBuilder();
        var errCount = _items.Count(d => d.Severity == ConversionDiagSeverity.Error);
        var warnCount = _items.Count - errCount;

        if (errCount > 0)
        {
            sb.Append("Conversion failed with ").Append(errCount).Append(" error");
            if (errCount != 1) sb.Append('s');
            if (warnCount > 0) sb.Append(" and ").Append(warnCount).Append(" warning").Append(warnCount != 1 ? "s" : "");
            sb.Append(':');
        }
        else
        {
            sb.Append("Conversion completed with ").Append(warnCount).Append(" warning").Append(warnCount != 1 ? "s" : "").Append(':');
        }
        sb.AppendLine();

        var shown = Math.Min(maxShown, _items.Count);
        for (var i = 0; i < shown; i++)
        {
            var d = _items[i];
            sb.Append("  • [").Append(d.Severity).Append("] ");
            if (d.LineNumber is { } line) sb.Append("Line ").Append(line).Append(": ");
            sb.Append(d.Message);
            if (!string.IsNullOrEmpty(d.Code)) sb.Append(" (").Append(d.Code).Append(')');
            sb.AppendLine();
        }
        if (_items.Count > maxShown)
        {
            sb.Append("  • …and ").Append(_items.Count - maxShown)
              .AppendLine(" more (see Log/ for the full list).");
        }
        return sb.ToString().TrimEnd();
    }
}