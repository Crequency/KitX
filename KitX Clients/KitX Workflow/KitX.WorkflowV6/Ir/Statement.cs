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
// Per discussion notes §十二-K, control-flow primitives (if/switch/forEach/while/
// break/continue/exit) are first-class IR statement kinds — they do NOT route through
// IBuiltinFunction. Only pure/side-effect functions (Print/Range/StringConcat/...) do.
// The StatementKind enum below is the discriminant for that split: any code that needs
// to dispatch on "what kind of statement is this" (Fingerprint.Compute, the BP renderer,
// the C# codegen, the structural-reduction check) switches on StatementKind rather than
// doing C#-type pattern matching, so the dispatch surface is explicit and stable across
// serialisation.
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
/// Discriminant for the closed set of structured IR statement kinds. Used by
/// <see cref="Fingerprint.Compute(Statement)"/>, the BP renderer, the structured-C#
/// codegen, and the structural-reduction check so they can dispatch without relying on
/// C# pattern matching (keeps the dispatch surface explicit and wire-stable).
/// </summary>
public enum StatementKind
{
    /// <summary>A <see cref="PipelineStatement"/> — functional `&gt;` data-flow (includes bare calls, assignments, multi-step pipelines).</summary>
    Pipeline,

    /// <summary>An <see cref="IfStatement"/> — structured if/else.</summary>
    If,

    /// <summary>A <see cref="SwitchStatement"/> — structured N-way dispatch.</summary>
    Switch,

    /// <summary>A <see cref="ForEachStatement"/> — structured collection iteration.</summary>
    ForEach,

    /// <summary>A <see cref="WhileStatement"/> — structured conditional loop.</summary>
    While,

    /// <summary>A <see cref="BreakStatement"/> — escapes the enclosing loop.</summary>
    Break,

    /// <summary>A <see cref="ContinueStatement"/> — skips to the next iteration of the enclosing loop.</summary>
    Continue,

    /// <summary>An <see cref="ExitStatement"/> — terminates the workflow (v5 "Break" builtin renamed).</summary>
    Exit,
}

/// <summary>
/// Common shape of every structured IR statement. Concrete statement kinds live in
/// the <c>Ir.Statements</c> folder (IfStatement / ForEachStatement / PipelineStatement / ...).
/// Each concrete record overrides <see cref="Kind"/> to return its discriminant.
/// </summary>
public abstract record Statement
{
    /// <summary>
    /// The discriminant for this statement kind. Always returns the same value for a
    /// given concrete type (e.g. <see cref="IfStatement.Kind"/> == <see cref="StatementKind.If"/>).
    /// Used for switch-dispatch by the fingerprint algorithm, BP renderer, codegen,
    /// and the structural-reduction check (discussion notes §十二-K).
    /// </summary>
    public abstract StatementKind Kind { get; }

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