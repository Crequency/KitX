namespace KitX.WorkflowV6.Lens.KsTextLens;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Lowering;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// KsTextLens — KS text ↔ structured IR (v6).
//
// Ported contract from archived v5.1 KitX.WorkflowIR.Lens.KsTextLens.KsTextLens: this is the
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
    /// Returns the content-addressed diff for the SyncService to apply — a re-parse +
    /// <see cref="WorkflowDiffer.Compute"/> structural diff.
    /// </summary>
    public WorkflowDiff Diff(Workflow baseline, string delta)
    {
        var newIr = Parse(delta, []);
        return WorkflowDiffer.Compute(baseline, newIr);
    }

    /// <summary>
    /// Parses KS source into a structured IR. Convenience entry that combines
    /// tokenize + parse + lower. Returns the IR even when there are diagnostics —
    /// the caller can inspect them via <see cref="ParseAstWithDiagnostics"/>.
    /// </summary>
    /// <param name="bpPrivileged">
    /// The pre-parse IR carrying BP-side privileged content that the KS text does NOT
    /// express (detached sub-graphs, B1). Re-parsing rebuilds the IR from scratch, so
    /// without this a KS-mode save/edit after a BP round-trip would silently drop the
    /// detached graphs — same re-attachment pattern as <see cref="BpGraphLens"/>'s
    /// <c>ksPrivileged</c> parameter (the two privileges are symmetric). Null keeps
    /// the previous behaviour (DetachedGraphs stay empty).
    /// </param>
    public Workflow Parse(string source, IReadOnlyList<HelperFunction> helpers, Workflow? bpPrivileged = null)
        => ParseLowering(source, helpers, bpPrivileged).Ir;

    /// <summary>
    /// Parses KS source into a structured IR and returns both the IR and the
    /// lowering result (PubVar type mapping). The lowering result is required by
    /// <see cref="Backend.RoslynBackend.StructuredRoslynBackend"/> for type-informed
    /// code generation. Unlike v5.1's <c>LoweringResult</c>, the v6 record does NOT
    /// carry the IR — callers receive a <c>(Workflow, LoweringResult)</c> tuple instead.
    /// </summary>
    public (Workflow Ir, LoweringResult Lowering) ParseLowering(
        string source, IReadOnlyList<HelperFunction> helpers, Workflow? bpPrivileged = null)
    {
        var (ast, diagnostics) = ParseAstWithDiagnostics(source);
        if (diagnostics.HasErrors)
        {
            var detail = string.Join("\n", diagnostics.Items
                .Where(d => d.Severity == KsDiagnosticSeverity.Error)
                .Select(d => $"  [{d.Code}] L{d.Line}: {d.Message}"));
            Log.Warning("[KsTextLens] Parse errors ({Count}) — IR may be incomplete:\n{Detail}",
                diagnostics.ErrorCount, detail);
        }
        var (ir, lowering) = new KsLowerer(_registry).Lower(ast, helpers);
        // BP-side privileged detached graphs (B1): not expressed in KS text, so a
        // re-parse drops them unless re-attached from the pre-parse IR — the symmetric
        // counterpart of the KS doc-comment re-attachment in BpGraphLens.Reverse*.
        if (bpPrivileged is { DetachedGraphs.Length: > 0 })
            ir = ir with { DetachedGraphs = bpPrivileged.DetachedGraphs };
        return (ir, lowering);
    }

    /// <summary>Parses KS source into the lossless KS AST (pre-lowering).</summary>
    public KsProgram ParseAst(string source)
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