namespace KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// Workflow — the top-level immutable container. The single source of truth.
//
// Inherited concept from KitX.WorkflowIR.IrWorkflow: the IR is the canonical
// representation that KS text, BP graph, and the execution backend all project from
// or write back to. Equality is structural; canvas layout (in Annotations) does not
// affect equality.
//
// Where v5 (KitX.WorkflowIR) modelled the workflow as a list of named Blocks plus
// Goto edges (a flat CFG), v6 models it as a single structured AST: the
/// <see cref="Body"/> is an ordered list of <see cref="Statement"/>s, some of which
/// (IfStatement / ForEachStatement / ...) contain their own nested bodies. There are
// no blocks, no block names, no Goto. Control flow is purely lexical.
//
// The HelperFunctions, Constants, and GlobalVars dictionaries come over from v5
// unchanged: helpers are still Contract-typed (see KitX.Core.Contract.Workflow),
// constants are still name-keyed, globals are still name-keyed. Only the body shape
// changes.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The immutable workflow IR — the canonical representation that KS, BP, and the
/// execution backend all project from / write back to. Equality is structural:
/// two workflows with the same body / constants / globals / helpers are equal, and
/// canvas layout (in <see cref="Annotations"/>) does not affect equality.
/// </summary>
public sealed record Workflow
{
    /// <summary>
    /// The structured top-level body: an ordered list of statements. Control flow is
    /// lexical (nested AST), not block + Goto.
    /// </summary>
    public ImmutableArray<Statement> Body { get; init; } = [];

    /// <summary>Constants from the KS source, keyed by name.</summary>
    public ImmutableDictionary<string, Constant> Constants { get; init; }
        = ImmutableDictionary<string, Constant>.Empty;

    /// <summary>Global mutable variables, keyed by name.</summary>
    public ImmutableDictionary<string, GlobalVar> GlobalVars { get; init; }
        = ImmutableDictionary<string, GlobalVar>.Empty;

    /// <summary>Helper functions available to the workflow (from Contract, unchanged).</summary>
    public ImmutableArray<HelperFunction> HelperFunctions { get; init; }
        = ImmutableArray<HelperFunction>.Empty;

    /// <summary>
    /// Workflow-level view/render metadata (canvas viewport, expanded scopes, debug
    /// highlights, ...). EXCLUDED from record equality; survives identity-preserving
    /// IR updates and file round-trips.
    /// </summary>
    public ImmutableArray<Annotation> Annotations { get; init; } = [];

    // ── Equality: every field EXCEPT Annotations. ──

    public bool Equals(Workflow? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!Body.SequenceEqual(other.Body)) return false;
        if (!HelperFunctions.SequenceEqual(other.HelperFunctions)) return false;
        if (Constants.Count != other.Constants.Count) return false;
        foreach (var (k, v) in Constants)
            if (!other.Constants.TryGetValue(k, out var v2) || !v.Equals(v2)) return false;
        if (GlobalVars.Count != other.GlobalVars.Count) return false;
        foreach (var (k, v) in GlobalVars)
            if (!other.GlobalVars.TryGetValue(k, out var v2) || !v.Equals(v2)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var s in Body) hash.Add(s);
        foreach (var h in HelperFunctions) hash.Add(h);
        foreach (var (k, v) in Constants) { hash.Add(k); hash.Add(v); }
        foreach (var (k, v) in GlobalVars) { hash.Add(k); hash.Add(v); }
        return hash.ToHashCode();
    }
}

/// <summary>
/// A constant from a <c>const { ... }</c> block (discussion notes §十二-C). Preserves
/// both the raw C# initialiser expression (for lossless KS round-trip, keeping quoting
/// /escaping) and the evaluated default value (for execution). Shape inherited from
/// v5.1 <c>KitX.Workflow.Ir.IrConstant</c>, re-typed as a record with required fields.
/// </summary>
public sealed record Constant
{
    public required string Name { get; init; }
    /// <summary>C# type name (e.g. "int", "string", "double"). Carried into codegen for typed emission.</summary>
    public string Type { get; init; } = "object";
    /// <summary>Verbatim initialiser expression source text (e.g. <c>42</c>, <c>"hi"</c>). Null when unset.</summary>
    public string? InitialValueExpression { get; init; }
    /// <summary>Evaluated default value, when known at lowering time. Null when dynamic.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>True when the constant has any kind of initial value.</summary>
    public bool HasInitialValue =>
        DefaultValue is not null || !string.IsNullOrEmpty(InitialValueExpression);
}

/// <summary>
/// A global mutable variable from a <c>var { ... }</c> block (discussion notes §十二-C).
/// Carries its declared C# type so the codegen backend can emit a strongly-typed field
/// on the <c>G</c> class (discussion notes §十二-F: PubVar strong typing, replacing the
/// v5.1 dictionary + boxing with direct field reads/writes — 10-100x on tight loops).
/// Shape inherited from v5.1 <c>KitX.Workflow.Ir.IrGlobalVar</c>.
/// </summary>
public sealed record GlobalVar
{
    public required string Name { get; init; }
    public string Type { get; init; } = "object";
    public string? InitialValueExpression { get; init; }
    public object? DefaultValue { get; init; }
}
