namespace KitX.Workflow.Ir.Lowering;

using KitX.Workflow.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// Lowering input / result — the explicit-parameter replacement for the legacy
// ForwardConversionState grab-bag.
//
// The legacy ForwardConversionState was one mutable object playing three roles at
// once: (1) input (Script, HelperFunctions), (2) Phase-1 output (ConstNodes,
// VariableNodes, PubVarNames), (3) Phase-2 output (FormattedScript CFG), plus a
// shared mutable counter and a diagnostics bag. It was passed by reference to 13
// call sites, so any of them could mutate any field at any time — the classic
// "shotgun-parameter" smell that made the lowering layer impossible to reason
// about locally.
//
// The new design splits these roles into three orthogonal types:
//
//   • LoweringInput  — immutable, what the lowering starts from.
//   • LoweringResult — immutable, what the lowering produces.
//   • PubVarAllocator / LoweringContext — controlled, locally-scoped mutable
//     helpers that the lowering thread owns; they never escape the lowering call.
//
// No type plays more than one role. The lowering function signature becomes
// `LoweringResult Lower(LoweringInput)` — a pure-ish function with all mutation
// confined to its own locals.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Immutable input to the BS→IR lowering. Carries exactly what the lowering needs
/// to start; nothing it produces. Replaces the input half of ForwardConversionState.
/// </summary>
public sealed record LoweringInput
{
    /// <summary>The parsed BS document (the parse tree from BsParser).</summary>
    public required BlockScript Script { get; init; }

    /// <summary>Helper functions available to the script (from Contract).</summary>
    public IReadOnlyList<HelperFunction> HelperFunctions { get; init; } = [];
}

/// <summary>
/// Immutable result of the BS→IR lowering. Replaces the output half of
/// ForwardConversionState (FormattedScript + PubVarTypes + PubVarNames + the
/// ConstNodes/VariableNodes dictionaries, which were only ever consumed to build
/// those very outputs).
/// </summary>
public sealed record LoweringResult
{
    /// <summary>The lowered immutable workflow IR.</summary>
    public required IrWorkflow Ir { get; init; }

    /// <summary>
    /// PubVar types keyed by variable name. Populated from the parsed #PubVarBlock
    /// and from any capacitor variables the lowering synthesised, so the C# backend
    /// can emit strong types. Capacitor names (vaaa####) default to "object".
    /// </summary>
    public IReadOnlyDictionary<string, string> PubVarTypes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// The set of PubVar names known to the lowering (declared PubVars + synthesised
    /// capacitors). Used by the C# backend and by the round-trip tests to tell
    /// PubVars from bare identifiers. Capacitor-only membership is permitted.
    /// </summary>
    public IReadOnlySet<string> PubVarNames { get; init; } = new HashSet<string>();

    /// <summary>
    /// Variable names injected at runtime by flow-control functions (e.g. ForLoop's
    /// indexName). Consumed by C# codegen to emit <c>G.Get("name")</c> for these.
    /// </summary>
    public IReadOnlySet<string> InjectedVariableNames { get; init; } = new HashSet<string>();

    /// <summary>User-facing diagnostics accumulated during lowering.</summary>
    public IReadOnlyList<LoweringDiagnostic> Diagnostics { get; init; } = [];

    // ── Equality: IReadOnlyDictionary/Set/List default Equals is reference equality,
    // so override to compare by content. Ir already has content-aware Equals.

    public bool Equals(LoweringResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!Ir.Equals(other.Ir)) return false;
        if (PubVarTypes.Count != other.PubVarTypes.Count) return false;
        foreach (var (k, v) in PubVarTypes)
            if (!other.PubVarTypes.TryGetValue(k, out var v2) || v != v2) return false;
        if (PubVarNames.Count != other.PubVarNames.Count) return false;
        foreach (var n in PubVarNames) if (!other.PubVarNames.Contains(n)) return false;
        if (InjectedVariableNames.Count != other.InjectedVariableNames.Count) return false;
        foreach (var n in InjectedVariableNames) if (!other.InjectedVariableNames.Contains(n)) return false;
        if (Diagnostics.Count != other.Diagnostics.Count) return false;
        for (int i = 0; i < Diagnostics.Count; i++)
            if (!Diagnostics[i].Equals(other.Diagnostics[i])) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Ir);
        foreach (var (k, v) in PubVarTypes) { hash.Add(k); hash.Add(v); }
        foreach (var n in PubVarNames) hash.Add(n);
        foreach (var n in InjectedVariableNames) hash.Add(n);
        foreach (var d in Diagnostics) hash.Add(d);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One diagnostic message from lowering. Replaces the legacy ConversionDiagnostics
/// bag (which mixed severity, line, code, and message into a mutable list). The
/// C# backend / BS renderer only need a flat list of these.
/// </summary>
public sealed record LoweringDiagnostic(
    LoweringDiagnosticSeverity Severity,
    string Code,
    string Message,
    int? Line = null);

public enum LoweringDiagnosticSeverity { Info, Warning, Error }
