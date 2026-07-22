namespace KitX.WorkflowV6.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// LoweringResult — the post-lowering artefacts the execution backend reuses.
//
// Inherited concept from KitX.WorkflowIR: lowering the KS source to IR produces not
// just the IR tree but also a set of by-products (PubVar type inference map, helper
// return-type map, names of variables injected at runtime like the ForEach element
// binding). These flow into the backend so generated C# can resolve typed reads and
/// plugin calls. Refined during the implementation phase.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Post-lowering by-products passed to the execution backend.</summary>
public sealed record LoweringResult
{
    /// <summary>Inferred C# type name per PubVar / Global identifier.</summary>
    public IReadOnlyDictionary<string, string> PubVarTypes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Return type per helper function name.</summary>
    public IReadOnlyDictionary<string, string> HelperReturnTypes { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Variable names injected at runtime (e.g. forEach element bindings).</summary>
    public IReadOnlySet<string> InjectedVariableNames { get; init; } = new HashSet<string>();
}
