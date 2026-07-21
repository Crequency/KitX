namespace KitX.WorkflowV6.Lens.BsTextLens;

using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// BsTextLens — BS text ↔ structured IR (v6).
//
// Inherited contract from KitX.WorkflowIR.Lens.BsTextLens.BsTextLens: this is the
// bidirectional bridge between the structured IR and the BS source text. The two
// hard responsibilities are unchanged:
//
//   • Project(ir)  → BS text     : render the IR back as indented BS source.
//   • Diff(base,δ) → WorkflowDiff: re-parse a BS edit, diff against the baseline IR,
//                                   return a content-addressed diff the SyncService
//                                   applies via the pure WorkflowDiffer.
//
// The grammar this lens parses is the v6 *indented* grammar (discussion notes §4.1),
// not the v5 block + Goto grammar. The parser stack (tokenizer, combinator, AST
// builder) is the open design surface (discussion notes §10.8). Whether it builds on
// Superpower or a hand-rolled indented parser is undecided; Superpower is referenced
// by the csproj so either path remains open.
//
// Method bodies are NotImplemented pending the implementation plan.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// BS text ↔ structured-IR lens for the v6 indented grammar. Methods are placeholders
/// pending the implementation plan; their signatures match the v5 contract so callers
/// (SyncService, WorkflowSession) can be wired in DI now.
/// </summary>
public sealed class BsTextLens : ILens<string, string>
{
    private readonly BuiltinFunctionRegistry _registry;

    public BsTextLens(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Renders the structured IR as indented BS source text.</summary>
    public string Project(Workflow ir) =>
        throw new NotImplementedException("BsTextLens.Project: v6 indented renderer not implemented.");

    /// <summary>
    /// Re-parses the edited BS text and diffs against <paramref name="baseline"/>.
    /// Returns the content-addressed diff for the SyncService to apply.
    /// </summary>
    public WorkflowDiff Diff(Workflow baseline, string delta) =>
        throw new NotImplementedException("BsTextLens.Diff: v6 indented parser not implemented.");

    /// <summary>
    /// Parses BS source into a structured IR. Convenience entry that combines parse +
    /// lowering; matches the shape WorkflowIR exposed for the SyncService.
    /// </summary>
    public Workflow Parse(string source, IReadOnlyList<HelperFunction> helpers) =>
        throw new NotImplementedException("BsTextLens.Parse: v6 indented parser not implemented.");

    /// <summary>Parses BS source into the lossless BS AST (pre-lowering).</summary>
    public BsNode ParseAst(string source) =>
        throw new NotImplementedException("BsTextLens.ParseAst: v6 indented parser not implemented.");
}
