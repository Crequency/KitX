namespace KitX.WorkflowV6.Ir.Lowering;

// ─────────────────────────────────────────────────────────────────────────────
// LoweringResult — the post-lowering artefacts the execution backend reuses.
//
// Inherited concept from KitX.WorkflowIR: lowering the KS source to IR produces not
// just the IR tree but also the PubVar type inference map consumed by the generated
// C# so typed reads resolve correctly. Refined during the implementation phase.
// (Helper return types / injected variable names are read directly from the IR by
// consumers and no longer carried here.)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Post-lowering by-products passed to the execution backend.</summary>
public sealed record LoweringResult
{
    /// <summary>Inferred C# type name per PubVar / Global identifier.</summary>
    public IReadOnlyDictionary<string, string> PubVarTypes { get; init; }
        = new Dictionary<string, string>();
}
