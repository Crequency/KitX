namespace KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// Statement — the common base of every node in the structured IR tree.
//
// Where the v5 IR (KitX.WorkflowIR) flattened control flow into named blocks plus
// Goto terminators, the v6 IR keeps control flow *lexical*: an IfStatement contains
// its branches as child Statements, a ForEachStatement contains its body as child
// Statements, and so on. There is no block-name addressing, no Goto, no trampoline
// switch. The whole tree is a single structured AST whose root is the workflow body.
//
// This mirrors the design captured in `Structured-BS-Discussion-Notes.md` §3–§4:
// 9 control-flow primitives (Sequence / if/else / switch / forEach / while / break /
// continue / exit / Range-as-function), indentation expressing scope, and 1:1 mapping
// to both BS text and BP node graph.
//
// Identity: every statement carries a <see cref="Fingerprint"/> (content-derived,
// re-parse-stable). The legacy random-Guid StatementId is gone, inherited from v5.
//
// View state (canvas positions, comments, source-position metadata) is EXCLUDED from
// equality: it lives in <see cref="Annotations"/>, exactly as in v5's IrBlock. This
// keeps semantic equality crisp (re-rendered graphs equal their originals) while
// preserving view state across IR updates and file round-trips.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Common shape of every structured IR statement. Concrete statement kinds live in
/// the <c>Ir.Statements</c> folder (IfStatement / ForEachStatement / PipelineStatement / ...).
/// </summary>
public abstract record Statement
{
    /// <summary>Content-derived, re-parse-stable identity. See <see cref="Fingerprint"/>.</summary>
    public required Fingerprint Fingerprint { get; init; }

    /// <summary>Free-form comment attached to this statement (round-trips through BS text).</summary>
    public string? Comment { get; init; }

    /// <summary>1-based source line in the original BS text, if known.</summary>
    public int SourceLine { get; init; }

    /// <summary>
    /// View/render metadata for this statement (canvas position, expanded/collapsed state,
    /// debug highlights, ...). EXCLUDED from record equality: it is view state, not semantics.
    /// </summary>
    public ImmutableArray<Annotation> Annotations { get; init; } = [];
}
