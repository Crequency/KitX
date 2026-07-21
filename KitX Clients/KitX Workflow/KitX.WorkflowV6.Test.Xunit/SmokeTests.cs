// ─────────────────────────────────────────────────────────────────────────────
// Smoke tests for the KitX.WorkflowV6 scaffolding.
//
// The current library ships no real behaviour (all bodies throw NotImplementedException
// or return empty). These tests assert the load-bearing invariants that DO hold today:
//
///   • The assembly loads and the basic types construct.
//   • BuiltinFunctionRegistry.Discover returns an empty registry (no builtins shipped yet).
//   • WorkflowDiffer.Compute returns an empty diff for two empty workflows.
//   • Workflow equality is structural (two empty workflows are equal; view-state-only
//     differences do not affect equality).
//
// Real per-component tests (BS lens round-trip, BP structural reduction, diff alignment,
// structured-C# backend, ...) ship with the implementation plan.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
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
            Value = new AnnotationValue { AnnotationKind = AnnotationKind.Layout, X = 1, Y = 2 },
        };

        var a = new Workflow();
        var b = new Workflow { Annotations = [layout] };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Pipeline_Statements_With_Same_Content_Are_Equal()
    {
        var fp = Fingerprint.Compute("Print(\"hello\")");
        var a = new PipelineStatement
        {
            Fingerprint = fp,
            Sources = ["\"hello\""],
            Segments = [new Segment { Target = "Print" }],
        };
        var b = new PipelineStatement
        {
            Fingerprint = fp,
            Sources = ["\"hello\""],
            Segments = [new Segment { Target = "Print" }],
        };
        Assert.Equal(a, b);
    }

    [Fact]
    public void If_Statement_Branch_Difference_Affects_Equality()
    {
        var fp = Fingerprint.Compute("if cond");
        var printStmt = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("Print(\"a\")"),
            Sources = ["\"a\""],
            Segments = [new Segment { Target = "Print" }],
        };

        var a = new IfStatement
        {
            Fingerprint = fp,
            Condition = "cond",
            ThenBody = [printStmt],
        };
        var b = new IfStatement
        {
            Fingerprint = fp,
            Condition = "cond",
            ThenBody = [],  // different body
        };
        Assert.NotEqual(a, b);
    }
}
