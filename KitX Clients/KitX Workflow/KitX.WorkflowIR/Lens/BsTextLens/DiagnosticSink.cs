using KitX.Workflow.Ir.Lowering;

namespace KitX.Workflow.Lens.BsTextLens;

// ─────────────────────────────────────────────────────────────────────────────
// DiagnosticSink — the parse-layer accumulator.
//
// The legacy KitX.Workflow carried a mutable ConversionDiagnostics bag (severity
// + line + code + message) that was shared across parse, conversion, and
// codegen. The new IR split that into LoweringDiagnostic (a record on the
// lowering result). The BS text lens, however, parses BEFORE lowering and needs
// a place to collect diagnostics as it goes — this sink is that place.
//
// It is a thin wrapper over List<LoweringDiagnostic> exposing the same
// AddError/AddWarning/HasErrors surface the migrated parser/analyser code
// expects, so the migrated A-grade logic reads almost identically to the
// legacy source. The sink is then handed off to the caller as the parse result.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Mutable accumulator for parse-layer diagnostics. Wraps a List&lt;LoweringDiagnostic&gt;
/// with the AddError/AddWarning/HasErrors convenience API the migrated parser expects.
/// </summary>
internal sealed class DiagnosticSink
{
    private readonly List<LoweringDiagnostic> _items = new();

    /// <summary>All diagnostics in insertion order.</summary>
    public IReadOnlyList<LoweringDiagnostic> Items => _items;

    /// <summary>Number of error diagnostics recorded.</summary>
    public int ErrorCount => _items.Count(d => d.Severity == LoweringDiagnosticSeverity.Error);

    /// <summary>True when at least one Error was recorded.</summary>
    public bool HasErrors => ErrorCount > 0;

    /// <summary>True when at least one Warning was recorded.</summary>
    public bool HasWarnings => _items.Any(d => d.Severity == LoweringDiagnosticSeverity.Warning);

    public IEnumerable<LoweringDiagnostic> Errors => _items.Where(d => d.Severity == LoweringDiagnosticSeverity.Error);
    public IEnumerable<LoweringDiagnostic> Warnings => _items.Where(d => d.Severity == LoweringDiagnosticSeverity.Warning);

    /// <summary>Records a diagnostic.</summary>
    public void Add(LoweringDiagnostic diag) => _items.Add(diag);

    /// <summary>Convenience: record an error diagnostic.</summary>
    public void AddError(string code, string message, int? lineNumber = null)
        => _items.Add(new LoweringDiagnostic(LoweringDiagnosticSeverity.Error, code, message, lineNumber));

    /// <summary>Convenience: record a warning diagnostic.</summary>
    public void AddWarning(string code, string message, int? lineNumber = null)
        => _items.Add(new LoweringDiagnostic(LoweringDiagnosticSeverity.Warning, code, message, lineNumber));

    /// <summary>Appends all diagnostics from another sink.</summary>
    public void AddRange(DiagnosticSink? other)
    {
        if (other is null) return;
        _items.AddRange(other._items);
    }
}
