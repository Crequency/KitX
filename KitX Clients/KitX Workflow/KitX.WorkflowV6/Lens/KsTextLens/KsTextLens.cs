namespace KitX.WorkflowV6.Lens.KsTextLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// KsTextLens — KS text ↔ structured IR (v6).
//
// Inherited contract from KitX.WorkflowIR.Lens.KsTextLens.KsTextLens: this is the
// bidirectional bridge between the structured IR and the KS source text. The two
// hard responsibilities are:
//
//   • Project(ir)  → KS text     : render the IR back as indented KS source.
//   • Diff(base,δ) → WorkflowDiff: re-parse a KS edit, diff against the baseline IR,
//                                  return a content-addressed diff the SyncService
//                                  applies via the pure WorkflowDiffer.
//
// The grammar this lens parses is the v6 *indented* grammar (discussion notes §4.1,
// §十二-A: 4-space indent, no tabs). The pipeline is Tokenizer → Parser → KsLowerer.
//
// Per discussion notes §十二-K, control-flow keywords (if/switch/forEach/while/
// break/continue) are NOT routed through the builtin registry — the parser
// builds their AST node kinds directly, and the lowerer lowers them into the
// first-class IR statement kinds.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// KS text ↔ structured-IR lens for the v6 indented grammar. Combines the tokenizer,
/// parser, lowerer, and renderer into the lens contract.
/// </summary>
public sealed class KsTextLens : ILens<string, string>
{
    private readonly BuiltinFunctionRegistry _registry;

    public KsTextLens(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Renders the structured IR as indented KS source text.</summary>
    public string Project(Workflow ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        return new KsRenderer().Render(ir);
    }

    /// <summary>
    /// Re-parses the edited KS text and diffs against <paramref name="baseline"/>.
    /// Returns the content-addressed diff for the SyncService to apply. The actual
    /// WorkflowDiffer.Compute is Phase 5; for now we round-trip via re-parse +
    /// structural equality so the SyncService contract compiles end-to-end.
    /// </summary>
    public WorkflowDiff Diff(Workflow baseline, string delta)
    {
        // Phase 5 fills in the real diff. For now we re-parse and return an empty
        // diff so the SyncService contract is wireable.
        var newIr = Parse(delta, []);
        return WorkflowDiffer.Compute(baseline, newIr);
    }

    /// <summary>
    /// Parses KS source into a structured IR. Convenience entry that combines
    /// tokenize + parse + lower. Returns the IR even when there are diagnostics —
    /// the caller can inspect <see cref="KsParseResult.Diagnostics"/>.
    /// </summary>
    public Workflow Parse(string source, IReadOnlyList<HelperFunction> helpers)
    {
        var (ast, parseDiag) = ParseAstWithDiagnostics(source);
        var lowerer = new KsLowerer(_registry);
        var (ir, lowerResult) = lowerer.Lower(ast, helpers);
        return ir;
    }

    /// <summary>Parses KS source into the lossless KS AST (pre-lowering).</summary>
    public KsNode ParseAst(string source)
    {
        var (ast, _) = ParseAstWithDiagnostics(source);
        return ast;
    }

    /// <summary>
    /// Parses KS source and returns both the AST and the collected diagnostics.
    /// Public low-level entry point for diagnostic inspection (e.g. asserting on
    /// specific error codes in tests, or surfacing diagnostics to IDE integrations).
    /// The higher-level <see cref="Parse"/> / <see cref="ParseAst"/> swallow
    /// diagnostics and return only the AST.
    /// </summary>
    public (KsProgram Ast, KsDiagnosticSink Diagnostics) ParseAstWithDiagnostics(string source)
    {
        var (tokens, tokDiag) = Tokenizer.Tokenize(source);
        var (ast, parseDiag) = Parser.Parse(tokens, tokDiag);
        return (ast, parseDiag);
    }
}

/// <summary>Result of a KS parse (AST + diagnostics).</summary>
public sealed record KsParseResult
{
    public required KsProgram Ast { get; init; }
    public required IReadOnlyList<KsDiagnostic> Diagnostics { get; init; }
}