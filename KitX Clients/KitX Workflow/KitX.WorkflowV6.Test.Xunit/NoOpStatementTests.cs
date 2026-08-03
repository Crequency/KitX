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

    // ── Multi-input function source order follows PIN order, not exec order ──

    private static BlueprintPin ExecPin(BlueprintNode node, bool output)
    {
        var pins = output ? node.OutputPins : node.InputPins;
        return pins.First(p => p.Type == PinType.Execution);
    }

    /// <summary>The unique EXEC edge between two nodes (data edges share the same node pair).</summary>
    private static BlueprintConnection ExecEdge(Blueprint bp, string srcId, string tgtId)
        => bp.Connections.Single(c => c.SourceNodeId == srcId && c.TargetNodeId == tgtId
                                     && bp.Nodes.First(n => n.Id == c.SourceNodeId)
                                         .OutputPins.First(p => p.Id == c.SourcePinId).Type == PinType.Execution);

    /// <summary>Swaps the exec order between the two usage VariableNodes feeding a function
    /// (entry → a → b → cmp becomes entry → b → a → cmp).</summary>
    private static void SwapExecOrder(Blueprint bp, VariableNode a, VariableNode b, BuiltinFunctionNode cmp)
    {
        var entry = bp.Nodes.OfType<EntryNode>().Single();
        var entryToA = ExecEdge(bp, entry.Id, a.Id);
        var aToB = ExecEdge(bp, a.Id, b.Id);
        var bToCmp = ExecEdge(bp, b.Id, cmp.Id);
        bp.Connections.Remove(entryToA);
        bp.Connections.Remove(aToB);
        bp.Connections.Remove(bToCmp);
        bp.Connections.Add(new BlueprintConnection
        {
            Id = Guid.NewGuid().ToString(),
            SourceNodeId = entry.Id,
            SourcePinId = ExecPin(entry, output: true).Id,
            TargetNodeId = b.Id,
            TargetPinId = ExecPin(b, output: false).Id,
        });
        bp.Connections.Add(new BlueprintConnection
        {
            Id = Guid.NewGuid().ToString(),
            SourceNodeId = b.Id,
            SourcePinId = ExecPin(b, output: true).Id,
            TargetNodeId = a.Id,
            TargetPinId = ExecPin(a, output: false).Id,
        });
        bp.Connections.Add(new BlueprintConnection
        {
            Id = Guid.NewGuid().ToString(),
            SourceNodeId = a.Id,
            SourcePinId = ExecPin(a, output: true).Id,
            TargetNodeId = cmp.Id,
            TargetPinId = ExecPin(cmp, output: false).Id,
        });
    }

    [Fact]
    public void Reverse_Respects_Wired_Pin_Order_Not_Exec_Chain_Order()
    {
        // User scenario: a manual exec re-wire makes the exec chain run srcB before
        // srcA, while the DATA edges still feed Compare.A from a and Compare.B from b.
        // Reverse must emit `a, b > Compare(...)` (pin order) — the exec order must
        // not scramble which source lands on which placeholder.
        var bp = _fixture.BpLens.Project(ParseKS("""
            var {
                int a
                int b
            }
            a, b > Compare("BEQ", _, _)
            """));
        var a = bp.Nodes.OfType<VariableNode>().Single(n => n.VarName == "a" && !n.IsDefinition);
        var b = bp.Nodes.OfType<VariableNode>().Single(n => n.VarName == "b" && !n.IsDefinition);
        var cmp = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "Compare");

        // Swap exec order: a → b → Compare becomes b → a → Compare.
        SwapExecOrder(bp, a, b, cmp);

        var reversed = _fixture.BpLens.Reverse(bp);
        var pipe = Assert.IsType<PipelineStatement>(reversed.Body[0]);

        // Sources follow the wired pin declaration order (A feeds a, B feeds b) — NOT
        // the exec chain order (b first).
        Assert.Equal(2, pipe.Sources.Length);
        Assert.Equal("a", pipe.Sources[0].SourceText);
        Assert.Equal("b", pipe.Sources[1].SourceText);
        var seg = Assert.Single(pipe.Segments);
        Assert.Equal("Compare", seg.Target);
    }

    [Fact]
    public void MultiInput_Pipeline_RoundTrip_Is_Still_Diff_Empty()
    {
        // Regression: when exec order == pin order (the renderer's natural output),
        // the pin-order reordering is a no-op and the round-trip stays diff-empty.
        var ir = ParseKS("""
            var {
                int a
                int b
            }
            a, b > Compare("BEQ", _, _)
            """);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes");
    }

    [Fact]
    public void Reordered_Exec_Chain_Normalises_On_RoundTrip()
    {
        // After Reverse (pin order) → Project, the exec chain is re-normalised to the
        // pin order; the reordered BP and the round-tripped BP are then equivalent.
        var bp = _fixture.BpLens.Project(ParseKS("""
            var {
                int a
                int b
            }
            a, b > Compare("BEQ", _, _)
            """));
        var a = bp.Nodes.OfType<VariableNode>().Single(n => n.VarName == "a" && !n.IsDefinition);
        var b = bp.Nodes.OfType<VariableNode>().Single(n => n.VarName == "b" && !n.IsDefinition);
        var cmp = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "Compare");
        SwapExecOrder(bp, a, b, cmp);

        var reversed = _fixture.BpLens.Reverse(bp);
        var bp2 = _fixture.BpLens.Project(reversed);
        var reversed2 = _fixture.BpLens.Reverse(bp2);
        var diff = WorkflowDiffer.Compute(reversed, reversed2);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes");
    }
}
