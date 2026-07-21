// ─────────────────────────────────────────────────────────────────────────────
// Phase 5 acceptance tests for WorkflowDiffer + WorkflowDiffApply.
//
// Covers the diff engine and applier:
//   • Empty vs empty → empty diff
//   • Add one Print → 1 Added change
//   • Modify one Print → 1 Modified change
//   • Remove one Print → 1 Removed change
//   • Layout preserved for unchanged statements
//   • Apply(baseline, Compute(baseline, new)) ≡ new (idempotence)
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.BsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class DiffTests
{
    private static readonly BuiltinFunctionRegistry _registry = new();

    private static Workflow Parse(params string[] lines)
    {
        var src = string.Join('\n', lines) + '\n';
        var lens = new BsTextLens(_registry);
        return lens.Parse(src, []);
    }

    [Fact]
    public void Diff_Empty_Empty_Is_Empty()
    {
        var diff = WorkflowDiffer.Compute(new Workflow(), new Workflow());
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Diff_Add_One_Print()
    {
        var a = Parse("Print(\"a\")");
        var b = Parse("Print(\"a\")", "Print(\"b\")");
        var diff = WorkflowDiffer.Compute(a, b);
        Assert.Single(diff.StatementChanges);
        var change = diff.StatementChanges[0];
        Assert.Equal(DiffKind.Added, change.Kind);
        Assert.NotNull(change.NewValue);
    }

    [Fact]
    public void Diff_Remove_One_Print()
    {
        var a = Parse("Print(\"a\")", "Print(\"b\")");
        var b = Parse("Print(\"a\")");
        var diff = WorkflowDiffer.Compute(a, b);
        Assert.Single(diff.StatementChanges);
        Assert.Equal(DiffKind.Removed, diff.StatementChanges[0].Kind);
    }

    [Fact]
    public void Diff_Modify_One_Print()
    {
        var a = Parse("Print(\"a\")");
        var b = Parse("Print(\"b\")");
        var diff = WorkflowDiffer.Compute(a, b);
        // Same Kind (Pipeline), different fingerprint → Modified (not Remove+Add).
        var changes = diff.StatementChanges;
        Assert.Contains(changes, c => c.Kind == DiffKind.Modified);
    }

    [Fact]
    public void Diff_Unchanged_Not_Reported()
    {
        var a = Parse("Print(\"a\")", "Print(\"b\")", "Print(\"c\")");
        var b = Parse("Print(\"a\")", "Print(\"b\")", "Print(\"c\")");
        var diff = WorkflowDiffer.Compute(a, b);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Apply_Idempotent_Add()
    {
        var a = Parse("Print(\"a\")");
        var b = Parse("Print(\"a\")", "Print(\"b\")");
        var diff = WorkflowDiffer.Compute(a, b);
        var result = WorkflowDiffApply.Apply(a, diff);
        Assert.Equal(b, result);
    }

    [Fact]
    public void Apply_Idempotent_Remove()
    {
        var a = Parse("Print(\"a\")", "Print(\"b\")");
        var b = Parse("Print(\"a\")");
        var diff = WorkflowDiffer.Compute(a, b);
        var result = WorkflowDiffApply.Apply(a, diff);
        Assert.Equal(b, result);
    }

    [Fact]
    public void Apply_Idempotent_Modify()
    {
        var a = Parse("Print(\"a\")");
        var b = Parse("Print(\"b\")");
        var diff = WorkflowDiffer.Compute(a, b);
        var result = WorkflowDiffApply.Apply(a, diff);
        Assert.Equal(b, result);
    }

    [Fact]
    public void Apply_Layout_Preserved_For_Unchanged()
    {
        // Two Print statements; the first has a Layout annotation. Modify the second;
        // the first's Layout must survive the diff+apply round-trip.
        var lit = new BsLiteral { Kind = BsLiteralKind.String, Value = "a", SourceText = "\"a\"" };
        var lit2 = new BsLiteral { Kind = BsLiteralKind.String, Value = "b", SourceText = "\"b\"" };
        var layoutAnn = new Annotation
        {
            Kind = "Layout",
            Key = "node1",
            Value = AnnotationValue.Layout(100, 200),
        };
        var stmt1 = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [lit],
            Segments = [new Segment { Target = "Print", Arguments = [lit] }],
            Annotations = [layoutAnn],
        };
        stmt1 = stmt1 with { Fingerprint = Fingerprint.Compute(stmt1) };

        var stmt2Old = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [new BsLiteral { Kind = BsLiteralKind.String, Value = "b", SourceText = "\"b\"" }],
            Segments = [new Segment { Target = "Print" }],
        };
        stmt2Old = stmt2Old with { Fingerprint = Fingerprint.Compute(stmt2Old) };

        var baseline = new Workflow { Body = [stmt1, stmt2Old] };

        // Build new IR: same stmt1, modified stmt2 (Print("c") instead of Print("b"))
        var stmt2New = new PipelineStatement
        {
            Fingerprint = Fingerprint.Compute("placeholder"),
            Sources = [new BsLiteral { Kind = BsLiteralKind.String, Value = "c", SourceText = "\"c\"" }],
            Segments = [new Segment { Target = "Print" }],
        };
        stmt2New = stmt2New with { Fingerprint = Fingerprint.Compute(stmt2New) };
        var newIr = new Workflow { Body = [stmt1, stmt2New] };

        var diff = WorkflowDiffer.Compute(baseline, newIr);
        var result = WorkflowDiffApply.Apply(baseline, diff);

        // stmt1's Layout annotation must be preserved.
        var resultStmt1 = result.Body[0];
        Assert.Contains(resultStmt1.Annotations, a => a.Kind == "Layout" && a.Key == "node1");
    }
}