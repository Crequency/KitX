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
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.BsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BpGraphLensRoundTripTests
{
    private static BuiltinFunctionRegistry Registry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    private static BsTextLens BsLens() => new(Registry());

    private static Workflow ParseBS(string src) => BsLens().Parse(src, []);

    private static BpGraphLens Lens() => new(Registry());

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_Simple_Print()
    {
        var ir = ParseBS("Print(\"hello\")\n");
        var lens = Lens();
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes");
    }

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_If_Else()
    {
        // Simple literal condition avoids multi-arg function pin limitation (P2-8).
        var ir = ParseBS("""
            if true
                Print("yes")
            else
                Print("no")
            """);
        var lens = Lens();
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
        var ir = ParseBS("forEach Range(0, 3, 1) as i\n    i > Print\n");
        var lens = Lens();
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: {string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void IR_To_BP_To_IR_Is_Equivalent_Switch()
    {
        // Literal selector (no pre-assignment) to avoid pure-data-assignment
        // nodes that don't participate in the exec chain.
        var ir = ParseBS("""
            switch 1
                0:
                    Print("zero")
                1:
                    Print("one")
                default:
                    Print("other")
            """);
        var lens = Lens();
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.Single(reversed.Body.OfType<SwitchStatement>());
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty, $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: {string.Join(", ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}"))}");
    }

    [Fact]
    public void Reverse_Produces_NonEmpty_IR_From_NonEmpty_Blueprint()
    {
        var ir = ParseBS("Print(\"a\")\nPrint(\"b\")\n");
        var lens = Lens();
        var bp = lens.Project(ir);
        var reversed = lens.Reverse(bp);
        Assert.NotEmpty(reversed.Body);
        Assert.Equal(2, reversed.Body.Length);
    }

    [Fact]
    public void BP_Edit_Delete_Produces_IR_Diff()
    {
        // BP-first edit (DeleteNode) should produce a WorkflowDiff with a Removed change.
        var ir = ParseBS("Print(\"a\")\nPrint(\"b\")\n");
        var lens = Lens();
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
        var ir = ParseBS("Print(\"a\")\n");
        var lens = Lens();
        var edits = new BpEditAction[] { new AddNodeInBlock("/top", "Print") };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Added);
    }
}