// ─────────────────────────────────────────────────────────────────────────────
// Phase 1 acceptance tests for KitX.WorkflowV6.Ir.
//
// Covers the load-bearing invariants of the now-final IR data model:
//
//   • The assembly loads and the basic types construct.
//   • BuiltinFunctionRegistry.Discover returns an empty registry (no builtins shipped yet).
//   • WorkflowDiffer.Compute returns an empty diff for two empty workflows.
//   • Workflow equality is structural (two empty workflows are equal; view-state-only
//     differences do not affect equality).
//   • Each concrete Statement reports the right StatementKind discriminant.
//   • Fingerprint is re-parse-stable: same content → same fingerprint.
//   • Fingerprint differs for different content (including nested-body differences).
//
// Real per-component tests (BS lens round-trip, BP structural reduction, diff alignment,
// structured-C# backend, ...) ship with the later implementation phases.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class SmokeTests
{
    [Fact]
    public void Empty_Registry_Discovered_From_V6_Assembly()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        Assert.NotNull(registry);
        Assert.Empty(registry.AllNames);
    }

    [Fact]
    public void Empty_Workflows_Have_Empty_Diff()
    {
        var a = new Workflow();
        var b = new Workflow();
        var diff = WorkflowDiffer.Compute(a, b);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Empty_Workflows_Are_Equal()
    {
        var a = new Workflow();
        var b = new Workflow();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Annotation_Only_Difference_Does_Not_Affect_Equality()
    {
        // View state (Annotations) must not affect semantic equality.
        var layout = new Annotation
        {
            Kind = "Layout",
            Key = "Viewport",
            Value = AnnotationValue.Layout(1, 2),
        };

        var a = new Workflow();
        var b = new Workflow { Annotations = [layout] };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Pipeline_Statements_With_Same_Content_Are_Equal()
    {
        var a = MakePrintPipeline("\"hello\"");
        var b = MakePrintPipeline("\"hello\"");
        Assert.Equal(a, b);
    }

    [Fact]
    public void If_Statement_Branch_Difference_Affects_Equality()
    {
        var printStmt = MakePrintPipeline("\"a\"");

        var a = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(MakeIdentifier("cond")),
            Condition = MakeIdentifier("cond"),
            ThenBody = [printStmt],
        };
        var b = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(MakeIdentifier("cond")),
            Condition = MakeIdentifier("cond"),
            ThenBody = [],  // different body
        };
        Assert.NotEqual(a, b);
    }

    // ── Phase 1 acceptance tests ──

    [Fact]
    public void StatementKind_Discriminant_Uniquely_Identifies_Each_Statement()
    {
        // Every concrete statement kind must report the matching StatementKind discriminant,
        // so dispatch-on-discriminant (fingerprint, codegen, BP renderer, structural reduction)
        // never misroutes a statement.
        Assert.Equal(StatementKind.Pipeline,
            MakePrintPipeline("\"x\"").Kind);
        Assert.Equal(StatementKind.If,
            new IfStatement { Fingerprint = Fingerprint.Compute(MakeIdentifier("c")), Condition = MakeIdentifier("c"), ThenBody = [] }.Kind);
        Assert.Equal(StatementKind.Switch,
            new SwitchStatement { Fingerprint = Fingerprint.Compute(MakeIdentifier("s")), Selector = MakeIdentifier("s"), Arms = [] }.Kind);
        Assert.Equal(StatementKind.ForEach,
            new ForEachStatement { Fingerprint = Fingerprint.Compute(MakeIdentifier("src")), Source = MakeIdentifier("src"), ItemName = "i", Body = [] }.Kind);
        Assert.Equal(StatementKind.While,
            new WhileStatement { Fingerprint = Fingerprint.Compute(MakeIdentifier("c")), Condition = MakeIdentifier("c"), Body = [] }.Kind);
        Assert.Equal(StatementKind.Break, new BreakStatement { Fingerprint = Fingerprint.Compute("break") }.Kind);
        Assert.Equal(StatementKind.Continue, new ContinueStatement { Fingerprint = Fingerprint.Compute("continue") }.Kind);
        Assert.Equal(StatementKind.Exit, new ExitStatement { Fingerprint = Fingerprint.Compute("exit") }.Kind);
    }

    [Fact]
    public void Fingerprint_Stable_Across_Reparse()
    {
        // Same content → same fingerprint. Build the same IfStatement twice (two separate
        // C# object identities) and verify the fingerprint value is identical — this is
        // the precondition for diff alignment and BP-node correlation across BS re-parse.
        var cond = MakeIdentifier("cond");
        var body1 = MakePrintPipeline("\"hello\"");
        var body2 = MakePrintPipeline("\"hello\"");

        var a = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(MakeIdentifier("cond")),
            Condition = MakeIdentifier("cond"),
            ThenBody = [body1],
        };
        var b = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(MakeIdentifier("cond")),
            Condition = MakeIdentifier("cond"),
            ThenBody = [body2],
        };
        Assert.Equal(Fingerprint.Compute(a), Fingerprint.Compute(b));
    }

    [Fact]
    public void Fingerprint_Differs_For_Different_Content()
    {
        // Different content → different fingerprint. Three independent mutations:
        //   (a) different condition identifier
        //   (b) different body (Print("hello") vs Print("world"))
        //   (c) different statement kind (If vs While) with same condition
        // Each must yield a different fingerprint from the baseline IfStatement.
        var baseline = new IfStatement
        {
            Fingerprint = Fingerprint.Compute(MakeIdentifier("cond")),
            Condition = MakeIdentifier("cond"),
            ThenBody = [MakePrintPipeline("\"hello\"")],
        };
        var baseFp = Fingerprint.Compute(baseline);

        var diffCond = baseline with
        {
            Fingerprint = Fingerprint.Compute(MakeIdentifier("other")),
            Condition = MakeIdentifier("other"),
        };
        Assert.NotEqual(baseFp, Fingerprint.Compute(diffCond));

        var diffBody = baseline with
        {
            ThenBody = [MakePrintPipeline("\"world\"")],
        };
        diffBody = diffBody with { Fingerprint = Fingerprint.Compute(diffBody) };
        Assert.NotEqual(baseFp, Fingerprint.Compute(diffBody));

        var sameCondButWhile = new WhileStatement
        {
            Fingerprint = Fingerprint.Compute(MakeIdentifier("cond")),
            Condition = MakeIdentifier("cond"),
            Body = [MakePrintPipeline("\"hello\"")],
        };
        Assert.NotEqual(baseFp, Fingerprint.Compute(sameCondButWhile));
    }

    [Fact]
    public void ForEach_Statement_Carries_ItemType()
    {
        // ForEachStatement must carry an ItemType (PinType) — the v6 strong-typing hook
        // (discussion notes §十二-F) and the BP Current-element data-pin type (§十二-G).
        var fe = new ForEachStatement
        {
            Fingerprint = Fingerprint.Compute(MakeIdentifier("Range(0,10,1)")),
            Source = MakeIdentifier("Range(0,10,1)"),
            ItemName = "i",
            ItemType = PinType.Integer,
            Body = [],
        };
        Assert.Equal(PinType.Integer, fe.ItemType);
    }

    [Fact]
    public void AnnotationValue_Factories_Produce_Correct_Kind()
    {
        // The convenience factories on AnnotationValue must tag the AnnotationKind correctly
        // so downstream renderers can switch on the kind without re-inferring.
        Assert.Equal(AnnotationKind.Layout, AnnotationValue.Layout(1, 2).AnnotationKind);
        Assert.Equal(AnnotationKind.Text, AnnotationValue.TextValue("hi").AnnotationKind);
        Assert.Equal(AnnotationKind.Int, AnnotationValue.IntValueOf(42).AnnotationKind);
        Assert.Equal(AnnotationKind.Bool, AnnotationValue.BoolValueOf(true).AnnotationKind);
        Assert.True(AnnotationValue.BoolValueOf(true).BoolValue);
    }

    // ── Helpers ──

    private static BsIdentifier MakeIdentifier(string name) =>
        new() { Name = name, SourceText = name };

    private static PipelineStatement MakePrintPipeline(string literalSource)
    {
        var lit = new BsLiteral
        {
            Kind = BsLiteralKind.String,
            Value = literalSource.Trim('"'),
            SourceText = literalSource,
        };
        var seg = new Segment { Target = "Print", Arguments = [lit], RawArguments = [literalSource] };
        // Construct with a placeholder fingerprint, then recompute the real structural
        // fingerprint once the full statement (sources + segments) is assembled.
        var stmt = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [seg],
        };
        return stmt with { Fingerprint = Fingerprint.Compute(stmt) };
    }
}