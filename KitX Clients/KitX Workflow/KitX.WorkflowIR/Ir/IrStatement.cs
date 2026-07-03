namespace KitX.Workflow.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrStatement — the discriminated union replacing legacy CFGStatement.
//
// The legacy CFGStatement was a single mutable class with 14 nullable fields and
// *two implicit discriminants*: FlowControlShape (non-null → control flow) and
// PubVarTarget (non-null → assignment). Every consumer had to know which subset
// of fields was meaningful for the current shape, and it was easy to set the
// wrong combination. v5.0 already removed a redundant Kind enum; here we finish
// the job with a proper sealed hierarchy.
//
// Two top-level shapes:
//   • IrPipelineStatement  — the v5.0 functional `>` syntax, carried verbatim as
//                            a structured BSPipeline AST (NOT flattened eagerly).
//   • IrControlFlowStatement — Branch / ForLoop / Switch / Goto / Break / Exit,
//                            with a unified Targets list (replaces the legacy
//                            arms/TrueBlock/FalseBlock/LoopbackTarget quartet).
//
// Plain assignment / function calls are themselves pipeline statements with a
// single source and (typically) a single segment; the lowering layer flattens
// them to imperative form on demand.
//
// Identity: every statement carries an IrFingerprint (content-derived, stable
// across re-parse). The legacy random-Guid StatementId is gone.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Common shape of every IR statement. Note that <see cref="Comment"/> lives here
/// (it round-trips through BS text and is small), but layout coordinates do NOT —
/// they live in <see cref="IrAnnotation"/> on the containing block.
/// </summary>
public abstract record IrStatement
{
    /// <summary>Content-derived, re-parse-stable identity. See <see cref="IrFingerprint"/>.</summary>
    public required IrFingerprint Fingerprint { get; init; }

    /// <summary>Free-form comment attached to this statement (v5.0 §9 bidirectional retention).</summary>
    public string? Comment { get; init; }

    /// <summary>1-based source line in the original BS text, if known.</summary>
    public int SourceLine { get; init; }
}
