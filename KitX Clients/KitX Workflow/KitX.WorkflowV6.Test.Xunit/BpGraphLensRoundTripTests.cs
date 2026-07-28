// ─────────────────────────────────────────────────────────────────────────────
// Round-trip tests: IR → BP → IR equivalence via BpGraphLens.Project + Reverse.
//
// Closes the P0-2/P0-3 round-trip gaps from the handoff document: Project renders
// the IR as a Blueprint, Reverse reconstructs an IR from that Blueprint, and
// WorkflowDiffer.Compute(original, reversed) should be empty (structural equality).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class BpGraphLensRoundTripTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public BpGraphLensRoundTripTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private Workflow ParseKS(string src) => _fixture.KsLens.Parse(src, []);

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_Simple_Print()
    {
        var ir = ParseKS("Print(\"hello\")\n");
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes");
    }

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_If_Else()
    {
        // Simple literal condition avoids multi-arg function pin limitation (P2-8).
        var ir = ParseKS("""
            if true:
                Print("yes")
            else:
                Print("no")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: {string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_ForEach()
    {
        // forEach with Range(0, 3, 1) — now with named pins (From/To/Step) the
        // round-trip should be fully diff-empty.
        var ir = ParseKS("forEach Range(0, 3, 1) as i:\n    i > Print\n");
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: {string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_Pipeline_Condition()
    {
        // Variable-source pipeline condition `if a, b > Compare("BEQ")` must round-trip
        // as a KsPipeline condition (NOT a flat KsCall with variable args — that would
        // violate the v6 bracket-narrowing rule KS051 on re-parse). The reverse translator
        // canonicalises wired pins to explicit `_` placeholders (semantically unambiguous),
        // so we author the source in the explicit-`_` form to get a clean empty diff.
        var ir = ParseKS("""
            var {
                int a
                int b
            }
            if a, b > Compare("BEQ", _, _):
                Print("equal")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var iff = Assert.IsType<IfStatement>(reversed.Body[0]);
        // The condition must be a KsPipeline (variable sources + Compare segment), not a flat KsCall.
        var condPipe = Assert.IsType<KsPipeline>(iff.Condition);
        Assert.Equal(2, condPipe.Sources.Length);
        Assert.Single(condPipe.Segments);
        Assert.Equal("Compare", condPipe.Segments[0].Target);
        // The "BEQ" literal must be preserved as a segment argument (not lost).
        Assert.Contains(condPipe.Segments[0].Args, a => a is KsLiteral { Kind: KsLiteralKind.String });
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: {string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void Pipeline_Condition_Append_Form_Canonicalised_To_Explicit_Placeholder()
    {
        // The append form `Compare("BEQ")` (no `_`) is semantically equivalent to the
        // explicit-`_` form `Compare("BEQ", _, _)`. Through BP round-trip the reverse
        // translator canonicalises to the explicit-`_` form (semantically unambiguous).
        // This test documents that canonicalisation: the condition structure round-trips
        // to the explicit-`_` form (not byte-identical to the append-form source, but
        // semantically equal — both route a, b into the A, B pins).
        var ir = ParseKS("""
            var {
                int a
                int b
            }
            if a, b > Compare("BEQ"):
                Print("equal")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var iff = Assert.IsType<IfStatement>(reversed.Body[0]);
        var condPipe = Assert.IsType<KsPipeline>(iff.Condition);
        // The reverse canonicalises to explicit `_` placeholders for the A, B pins.
        Assert.Equal(2, condPipe.Segments[0].Args.Count(a => a is KsPlaceholder));
    }

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_Switch()
    {
        // Literal selector (no pre-assignment) to avoid pure-data-assignment
        // nodes that don't participate in the exec chain.
        var ir = ParseKS("""
            switch 1:
                0:
                    Print("zero")
                1:
                    Print("one")
                default:
                    Print("other")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Single(reversed.Body.OfType<SwitchStatement>());
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: {string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void Reverse_Produces_NonEmpty_IR_From_NonEmpty_Blueprint()
    {
        var ir = ParseKS("Print(\"a\")\nPrint(\"b\")\n");
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.NotEmpty(reversed.Body);
        Assert.Equal(2, reversed.Body.Length);
    }

    [Fact]
    public void BP_Edit_Delete_Produces_IR_Diff()
    {
        // BP-first edit (DeleteNode) should produce a WorkflowDiff with a Removed change.
        var ir = ParseKS("Print(\"a\")\nPrint(\"b\")\n");
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        // Delete the second Print node (find it by FunctionName).
        var printNodes = bp.Nodes.OfType<BuiltinFunctionNode>().Where(n => n.FunctionName == "Print").ToList();
        Assert.Equal(2, printNodes.Count);
        var edits = new BpEditAction[] { new DeleteNode(printNodes[1].Id) };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Removed);
    }

    [Fact]
    public void BP_Edit_Add_Produces_IR_Diff()
    {
        // BP-first edit (AddNodeInBlock) should produce a WorkflowDiff with an Added change.
        var ir = ParseKS("Print(\"a\")\n");
        var lens = _fixture.BpLens;
        var edits = new BpEditAction[] { new AddNodeInBlock("/top", "Print") };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Added);
    }

    // ── Comment preservation through BP round-trip (Phase B-2) ──

    [Fact]
    public void BP_LeadingComment_RoundTrip()
    {
        // A leading comment maps to a GroupComment (anchored to the statement's primary
        // node) and round-trips back as the statement's LeadingComment.
        var ir = ParseKS("""
            // group comment for the print
            Print("x")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);

        // Forward: the BP carries a GroupComment anchored to the Print node.
        Assert.Single(bp.GroupComments);
        var gc = bp.GroupComments[0];
        Assert.Equal("group comment for the print", gc.Comment);
        var printNode = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "Print");
        Assert.Equal(printNode.Id, gc.AnchorNodeId);

        // Reverse: the comment reattaches as the statement's LeadingComment.
        var reversed = lens.Reverse(bp);
        var pipe = Assert.IsType<PipelineStatement>(reversed.Body[0]);
        Assert.Equal("group comment for the print", pipe.LeadingComment);
    }

    [Fact]
    public void BP_TrailingComment_RoundTrip()
    {
        // A trailing comment maps to the primary node's Comment and round-trips.
        var ir = ParseKS("Print(\"x\") // trailing comment\n");
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);

        var printNode = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "Print");
        Assert.Equal("trailing comment", printNode.Comment);

        var reversed = lens.Reverse(bp);
        var pipe = Assert.IsType<PipelineStatement>(reversed.Body[0]);
        Assert.Equal("trailing comment", pipe.TrailingComment);
    }

    [Fact]
    public void BP_ControlFlow_Leading_And_Trailing_RoundTrip()
    {
        // Leading + trailing comments on a control-flow statement round-trip.
        var ir = ParseKS("""
            // guard the loop
            while true: // keep going
                Print("tick")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);

        var whileNode = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "While");
        Assert.Equal("keep going", whileNode.Comment);
        Assert.Single(bp.GroupComments);
        Assert.Equal("guard the loop", bp.GroupComments[0].Comment);
        Assert.Equal(whileNode.Id, bp.GroupComments[0].AnchorNodeId);

        var reversed = lens.Reverse(bp);
        var ws = Assert.IsType<WhileStatement>(reversed.Body[0]);
        Assert.Equal("guard the loop", ws.LeadingComment);
        Assert.Equal("keep going", ws.TrailingComment);
    }

    [Fact]
    public void BP_Condition_Segment_Comment_RoundTrip()
    {
        // A condition-function-node Comment maps to the condition pipeline's last
        // segment Segment.Comment (C-2). The KS syntax for condition segment comments
        // arrives in C-3, so here we set the node Comment manually on the BP and verify
        // the reverse translator reattaches it as the condition segment's comment.
        var ir = ParseKS("""
            var {
                int a
                int b
            }
            if a, b > Compare("BEQ"):
                Print("equal")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        // Manually annotate the Compare condition node (simulating a BP-side edit).
        var compareNode = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "Compare");
        compareNode.Comment = "check equality";

        var reversed = lens.Reverse(bp);
        var iff = Assert.IsType<IfStatement>(reversed.Body[0]);
        var condPipe = Assert.IsType<KsPipeline>(iff.Condition);
        Assert.Equal("check equality", condPipe.Segments[0].Comment);
    }

    // ── Multi-segment pipeline round-trip (the bug fixed by the pipeline-merging
    //    refactor of BpReverseTranslator — without merging, each segment node would
    //    be emitted as a standalone PipelineStatement). ──

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_Multi_Segment_Pipeline()
    {
        // `0 > Add(_, 1) > counter` has 2 segments (function call + var tap). The
        // reverse translator must merge ConstNode(0) → Add → counter into ONE
        // PipelineStatement, not split into separate statements.
        var ir = ParseKS("""
            var {
                int counter
            }
            0 > Add(_, 1) > counter
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Single(reversed.Body);  // critical: must be exactly one statement
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_Multi_Source_Multi_Segment_Pipeline()
    {
        // `a, b > Compare("BEQ", _, _) > cond`: 2 sources + 1 function segment + 1 var tap.
        // Uses explicit `_` placeholders (the canonical form BP→KS upgrades append-form
        // inputs to — see IR_To_BP_To_IR_Is_Equivalent_Pipeline_Condition for the same
        // canonicalisation note).
        var ir = ParseKS("""
            var {
                int a
                int b
                bool cond
            }
            a, b > Compare("BEQ", _, _) > cond
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Single(reversed.Body);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_Pipeline_With_Literal_And_Wired_Args()
    {
        // `loopMax > Range(0, _, 1) > items` exercises a function whose args mix
        // literal DefaultValues (0, 1) with a wired `_` placeholder. The reverse
        // translator must preserve the literal args + the explicit `_` position
        // (otherwise the append rule would route loopMax into the wrong pin).
        var ir = ParseKS("""
            var {
                int loopMax
                int items
            }
            loopMax > Range(0, _, 1) > items
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Single(reversed.Body);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_Two_Independent_Bare_Calls_Not_Merged()
    {
        // `Print("hello")\nPrint("world")` must round-trip as TWO statements, not
        // be merged into one. The pipeline-merging algorithm uses data-continuity
        // to decide merging; two unrelated bare calls have no data wire between them.
        var ir = ParseKS("Print(\"hello\")\nPrint(\"world\")\n");
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Equal(2, reversed.Body.Length);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes");
    }

    [Fact]
    public void IR_To_BP_To_IR_Multi_Statement_With_Pipeline_In_Middle()
    {
        // Mixed: bare call → multi-segment pipeline → bare call. Each statement
        // boundary must be respected.
        var ir = ParseKS("""
            var {
                int counter
            }
            Print("start")
            0 > Add(_, 1) > counter
            Print("end")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Equal(3, reversed.Body.Length);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    // ── End-pin model tests (v6.0 refactor) ──
    // These cover the Branch/Switch End pin refactor: post-construct statements connect
    // to the control-flow node's End pin (single continuation), and sub-scope body tails
    // dangle. The key scenario is control flow FOLLOWED BY more statements — the old
    // diamond-merge model had a known bug where continuation statements got embedded in
    // both ThenBody and ElseBody; the End-pin model fixes this.

    [Fact]
    public void IR_To_BP_To_IR_If_Else_With_Continuation()
    {
        // if/else followed by a statement — the post-if Print should round-trip as a
        // top-level statement, NOT be embedded in ThenBody/ElseBody.
        var ir = ParseKS("""
            var {
                bool cond
            }
            if cond:
                Print("then")
            else:
                Print("else")
            Print("after")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        // Top-level body should have exactly 3 statements: PipelineStatement(Print?),
        // IfStatement, PipelineStatement(Print after).
        Assert.Equal(2, reversed.Body.Length);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_If_No_Else_With_Continuation()
    {
        // if without else, followed by a statement.
        var ir = ParseKS("""
            var {
                bool cond
            }
            if cond:
                Print("then")
            Print("after")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Equal(2, reversed.Body.Length);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_Switch_With_Continuation()
    {
        // switch followed by a statement — the post-switch Print should round-trip as
        // a top-level statement, NOT be embedded in any arm body.
        var ir = ParseKS("""
            var {
                int sel
            }
            switch sel:
                0:
                    Print("zero")
                1:
                    Print("one")
                default:
                    Print("default")
            Print("after")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Equal(2, reversed.Body.Length);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_Nested_If_Inside_ForEach_With_Break()
    {
        // Nested control flow: forEach body contains an if with a break — the canonical
        // guess-number-game pattern. This stresses the End-pin model's scope stack
        // (break must be recognised as inside the forEach loop scope).
        // Uses explicit `_` placeholder form to avoid the append-form upgrade degradation
        // (§7.1 #1: `a, b > Compare("BEQ")` reverses to `Compare("BEQ", _, _)`).
        var ir = ParseKS("""
            const {
                int guessNum = 5
                int targetNum = 7
            }
            var {
                bool cond
            }
            forEach Range(0, 3, 1) as i:
                guessNum, targetNum > Compare("BEQ", _, _) > cond
                if cond:
                    Print("correct")
                    break
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_While_With_Continuation()
    {
        // while loop followed by a statement.
        var ir = ParseKS("""
            var {
                bool cond
            }
            while cond:
                Print("tick")
            Print("after")
            """);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Equal(2, reversed.Body.Length);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: " +
            $"{string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }
}