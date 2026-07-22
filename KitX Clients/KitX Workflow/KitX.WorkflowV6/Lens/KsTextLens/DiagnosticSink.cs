namespace KitX.WorkflowV6.Lens.KsTextLens;

// ─────────────────────────────────────────────────────────────────────────────
// KsDiagnostic — parse / lower time diagnostic message.
//
// Inherited shape from KitX.WorkflowIR.Ir.Lowering.LoweringDiagnostic, re-namespaced
// to the v6 KS text lens so the parser, lowerer, and lens can all emit diagnostics
// without pulling in the lowering namespace at every call site.
//
// Carries: severity (Info/Warning/Error), a short machine-readable Code, a
// human-readable Message, and the 1-based Line/Column where the issue begins.
// Column is 1-based and counts characters (post-indent), so a Tab-rejection
// diagnostic at column 1 of line 3 points exactly at the offending Tab.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One diagnostic message from KS parsing or lowering.</summary>
public sealed record KsDiagnostic
{
    public required KsDiagnosticSeverity Severity { get; init; }

    /// <summary>Short machine-readable code (e.g. "KS001" for Tab rejected).</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; }

    /// <summary>1-based source line, or null when unknown.</summary>
    public int? Line { get; init; }

    /// <summary>1-based source column, or null when unknown.</summary>
    public int? Column { get; init; }
}

/// <summary>Diagnostic severity (mirrors v5 LoweringDiagnosticSeverity).</summary>
public enum KsDiagnosticSeverity { Info, Warning, Error }

/// <summary>
/// Mutable accumulator for KS diagnostics, mirroring v5's KsDiagnosticSink. The
/// tokenizer, parser, and lowerer all write into one of these; the lens surfaces
/// the collected list to the caller as part of the parse result.
/// </summary>
internal sealed class KsDiagnosticSink
{
    private readonly List<KsDiagnostic> _items = new();

    public IReadOnlyList<KsDiagnostic> Items => _items;
    public int ErrorCount => _items.Count(d => d.Severity == KsDiagnosticSeverity.Error);
    public bool HasErrors => ErrorCount > 0;

    public void Add(KsDiagnostic diag) => _items.Add(diag);

    public void AddError(string code, string message, int? line = null, int? column = null)
        => _items.Add(new KsDiagnostic
        {
            Severity = KsDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            Line = line,
            Column = column,
        });

    public void AddWarning(string code, string message, int? line = null, int? column = null)
        => _items.Add(new KsDiagnostic
        {
            Severity = KsDiagnosticSeverity.Warning,
            Code = code,
            Message = message,
            Line = line,
            Column = column,
        });

    public void AddRange(KsDiagnosticSink? other)
    {
        if (other is null) return;
        _items.AddRange(other._items);
    }
}