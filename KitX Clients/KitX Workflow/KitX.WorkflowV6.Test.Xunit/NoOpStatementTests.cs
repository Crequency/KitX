// ─────────────────────────────────────────────────────────────────────────────
// NoOpStatement tests: a usage node on the exec chain with NO data edges.
//
// KS side: a single identifier/literal line (`a` / `5`) is a valid no-op statement
// (KS053 relaxed) — it is "just there": an exec anchor with no data flow.
// BP side: a read ConstNode/VariableNode on the exec chain without outgoing data
// edges. Reverse must split it into its own bare-line statement instead of merging
// it into the neighbouring pipeline (which would fabricate a data edge).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.BpGraphLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class NoOpStatementTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public NoOpStatementTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private Workflow ParseKS(string src) => _fixture.KsLens.Parse(src, []);

    [Fact]
    public void Bare_Identifier_Line_Parses_As_NoOp_Statement()
    {
        var ir = ParseKS("""
            var {
                int a
            }
            a
            """);

        var stmt = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Empty(stmt.Segments);
        Assert.Single(stmt.Sources);
        Assert.Equal("a", stmt.Sources[0].SourceText);
    }

    [Fact]
    public void Bare_Literal_Line_Parses_As_NoOp_Statement()
    {
        var ir = ParseKS("5\n");
        var stmt = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Empty(stmt.Segments);
        Assert.Single(stmt.Sources);
    }

    [Fact]
    public void Bare_Identifier_Line_RoundTrips_Through_BP()
    {
        // KS `a` (no-op) → BP (usage node on exec chain, no data edges) → KS.
        var ir = ParseKS("""
            var {
                int a
            }
            a
            Print("after")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void NoOp_Identifier_Before_Pipeline_Stays_Separate()
    {
        // User scenario: exec chain a → b → Print, only b's value flows into Print.
        // Reverse must NOT merge a into `a, b > Print` (that would fabricate a's edge).
        var ir = ParseKS("""
            var {
                int a
                int b
            }
            a
            b > Print
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);

        // Exec chain: a → b → Print; data edge: b → Print only.
        var aNode = Assert.Single(bp.Nodes, n => n is VariableNode { IsDefinition: false } vn && vn.VarName == "a");
        Assert.False(bp.Connections.Any(c => c.SourceNodeId == aNode.Id
                                             && !bp.Nodes.First(n => n.Id == c.SourceNodeId).OutputPins
                                                 .First(p => p.Id == c.SourcePinId).Type.Equals(PinType.Execution)));

        var reversed = lens.Reverse(bp);
        Assert.Equal(2, reversed.Body.Length);
        var noOp = Assert.IsType<PipelineStatement>(reversed.Body[0]);
        Assert.Empty(noOp.Segments);
        Assert.Single(noOp.Sources);
        Assert.Equal("a", noOp.Sources[0].SourceText);
        var pipe = Assert.IsType<PipelineStatement>(reversed.Body[1]);
        Assert.Single(pipe.Segments);

        // Full equivalence.
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void MultiSource_Pipeline_Still_Merges()
    {
        // Regression: genuine multi-source pipelines (every source has a data edge)
        // must still merge into one statement.
        var ir = ParseKS("""
            var {
                int a
                int b
            }
            a, b > Compare("BEQ", _, _)
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes");
        Assert.Single(reversed.Body);
    }

    [Fact]
    public async Task NoOp_Codegen_Is_A_Noop_Comment()
    {
        // The no-op statement compiles to a comment (no runtime effect).
        var ir = ParseKS("""
            var {
                int a
            }
            a
            """);
        var backend = _fixture.MakeBackend();
        var result = await backend.ExecuteAsync(ir, null, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Output);
    }
}
