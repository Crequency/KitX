namespace KitX.WorkflowV6.Lens.BsTextLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// BsTextLens — KS text ↔ structured IR (v6).
//
// Inherited contract from KitX.WorkflowIR.Lens.BsTextLens.BsTextLens: this is the
// bidirectional bridge between the structured IR and the KS source text. The two
// hard responsibilities are:
//
//   • Project(ir)  → KS text     : render the IR back as indented KS source.
//   • Diff(base,δ) → WorkflowDiff: re-parse a KS edit, diff against the baseline IR,
//                                  return a content-addressed diff the SyncService
//                                  applies via the pure WorkflowDiffer.
//
// The grammar this lens parses is the v6 *indented* grammar (discussion notes §4.1,
// §十二-A: 4-space indent, no tabs). The pipeline is Tokenizer → Parser → BsLowerer.
//
// Per discussion notes §十二-K, control-flow keywords (if/switch/forEach/while/
// break/continue/exit) are NOT routed through the builtin registry — the parser
// builds their AST node kinds directly, and the lowerer lowers them into the
// first-class IR statement kinds.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// KS text ↔ structured-IR lens for the v6 indented grammar. Combines the tokenizer,
/// parser, lowerer, and renderer into the lens contract.
/// </summary>
public sealed class BsTextLens : ILens<string, string>
{
    private readonly BuiltinFunctionRegistry _registry;

    public BsTextLens(BuiltinFunctionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Renders the structured IR as indented KS source text.</summary>
    public string Project(Workflow ir)
    {
        ArgumentNullException.ThrowIfNull(ir);
        return new BsRenderer().Render(ir);
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
    /// the caller can inspect <see cref="ParseResult.Diagnostics"/>.
    /// </summary>
    public Workflow Parse(string source, IReadOnlyList<HelperFunction> helpers)
    {
        var (ast, parseDiag) = ParseAstWithDiagnostics(source);
        var lowerer = new BsLowerer(_registry);
        var (ir, lowerResult) = lowerer.Lower(ast, helpers);
        return ir;
    }

    /// <summary>Parses KS source into the lossless KS AST (pre-lowering).</summary>
    public BsNode ParseAst(string source)
    {
        var (ast, _) = ParseAstWithDiagnostics(source);
        return ast;
    }

    /// <summary>
    /// Parses KS source and returns both the AST and the collected diagnostics.
    /// Internal — the public surface is <see cref="Parse"/> / <see cref="ParseAst"/>;
    /// diagnostics are surfaced via <see cref="ParseResult"/> once Phase 5 wires the
    /// SyncService to use them. For now the API is exposed as a low-level hook for
    /// tests that need to assert on diagnostics.
    /// </summary>
    internal (BsProgram Ast, DiagnosticSink Diagnostics) ParseAstWithDiagnostics(string source)
    {
        var (tokens, tokDiag) = Tokenizer.Tokenize(source);
        var (ast, parseDiag) = Parser.Parse(tokens, tokDiag);
        return (ast, parseDiag);
    }
}

/// <summary>Result of a KS parse (AST + diagnostics). Used internally + by tests.</summary>
internal sealed record ParseResult
{
    public required BsProgram Ast { get; init; }
    public required IReadOnlyList<BsDiagnostic> Diagnostics { get; init; }
}