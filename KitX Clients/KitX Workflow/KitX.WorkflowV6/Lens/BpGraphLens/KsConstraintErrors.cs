namespace KitX.WorkflowV6.Lens.BpGraphLens;

// ─────────────────────────────────────────────────────────────────────────────
// KsConstraintErrors — single source of truth for the BP-side constraint error
// codes (Kscript-Blueprint-GrammarRule.md §4.3). StructuralReducer emits the
// eleven "live" codes (KS100/101/102/105/110/111/112/113/120/130/140); the
// remaining codes are design-reserved (KS103/104 covered indirectly by the E2
// walk, KS121 an internal mechanism rather than an error code).
// ─────────────────────────────────────────────────────────────────────────────

internal static class KsConstraintErrors
{
    // ── Live codes (actually emitted by StructuralReducer) ──

    /// <summary>E1 — Connectivity: every non-definition node reachable from EntryNode.</summary>
    public const string KS100 = "KS100";

    /// <summary>E2 — Structural reducibility: exec graph reduces to a structured tree.</summary>
    public const string KS101 = "KS101";

    /// <summary>E3 — Unique predecessor: each Exec input has at most one incoming edge.</summary>
    public const string KS102 = "KS102";

    /// <summary>E6 — Back-edge rule: no explicit exec cycles; loops are implicit.</summary>
    public const string KS105 = "KS105";

    /// <summary>D1 — Data DAG: the data graph must be acyclic.</summary>
    public const string KS110 = "KS110";

    /// <summary>D2 — Single data input: each data input pin has at most one incoming edge.</summary>
    public const string KS111 = "KS111";

    /// <summary>D3 — Data-scope reachability: a data edge's source must be same-scope or outer relative to the consumer.</summary>
    public const string KS112 = "KS112";

    /// <summary>D4 — Condition sub-graph contained in the control-flow node's scope.</summary>
    public const string KS113 = "KS113";

    /// <summary>C1 — Every non-definition node must have Exec pins.</summary>
    public const string KS120 = "KS120";

    /// <summary>N2 — VarName consistency: usage VariableNode matches a definition VariableNode.</summary>
    public const string KS130 = "KS130";

    /// <summary>KS140 — break/continue must be inside a loop scope.</summary>
    public const string KS140 = "KS140";

    // ── Design-reserved codes (not independently emitted) ──

    /// <summary>E4 — Sub-scope termination: covered indirectly by the E2 walk, no standalone code.</summary>
    public const string KS103 = "KS103";

    /// <summary>E5 — Scope isolation: covered indirectly by the E2 walk, no standalone code.</summary>
    public const string KS104 = "KS104";

    /// <summary>C2 — Data sub-graph not independently present: internal consumed-marking mechanism, not an error code.</summary>
    public const string KS121 = "KS121";
}
