// ─────────────────────────────────────────────────────────────────────────────
// Phase 9 tests: BpGraphLens.Diff + StructuralReducer.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.BsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BpGraphLensDiffTests
{
    private static BpGraphLens Lens() => new(Registry());

    private static BuiltinFunctionRegistry Registry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    private static BsTextLens BsLens() => new(Registry());

    private static Workflow ParseBS(string src) => BsLens().Parse(src, []);

    [Fact]
    public void Diff_Empty_Edits_Returns_Empty_Diff()
    {
        var lens = Lens();
        var ir = ParseBS("Print(\"a\")\n");
        var diff = lens.Diff(ir, []);
        Assert.NotNull(diff);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Diff_Add_Node_Produces_Added_Change()
    {
        var lens = Lens();
        var ir = ParseBS("Print(\"a\")\n");
        var edits = new BpEditAction[] { new AddNodeInBlock("/top", "Print") };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Added);
    }

    [Fact]
    public void Diff_Delete_Node_Produces_Removed_Change()
    {
        var lens = Lens();
        var ir = ParseBS("Print(\"a\")\n");
        var edits = new BpEditAction[] { new DeleteNode("v6-/top/stmt/0") };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Removed);
    }

    [Fact]
    public void Structural_Simple_Pipeline_Valid()
    {
        var bp = new BpGraphLens(Registry()).Project(ParseBS("Print(\"hello\")\n"));
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_If_Else_Valid()
    {
        var bp = new BpGraphLens(Registry()).Project(ParseBS("if HelperFuncCompare(\"BEQ\", 1, 1)\n    Print(\"yes\")\n"));
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_ForEach_Valid()
    {
        var bp = new BpGraphLens(Registry()).Project(ParseBS("forEach Range(0, 5, 1) as i\n    i > Print\n"));
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_Non_Structural_Back_Edge_Rejected()
    {
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "entry", Name = "Start", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "entry-out", Name = "Exec", Direction = PinDirection.Output });
        var nodeA = MakeNode("a", "A");
        var nodeB = MakeNode("b", "B");
        bp.Nodes.Add(entry); bp.Nodes.Add(nodeA); bp.Nodes.Add(nodeB);
        // Entry -> A -> B -> Entry (cycle!)
        bp.Connections.Add(Conn("entry", "entry-out", "a", "a-in"));
        bp.Connections.Add(Conn("a", "a-out", "b", "b-in"));
        bp.Connections.Add(Conn("b", "b-out", "entry", "entry-in"));
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("back edge", error.ToLowerInvariant());
    }

    [Fact]
    public void Error_Message_Guides_To_Loop_Node()
    {
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Start", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output });
        var node = MakeNode("n", "N");
        node.InputPins.Add(new BlueprintPin { Id = "ni2", Name = "Exec", Direction = PinDirection.Input });
        bp.Nodes.Add(entry); bp.Nodes.Add(node);
        bp.Connections.Add(Conn("e", "eo", "n", "n-in"));
        bp.Connections.Add(Conn("n", "n-out", "n", "n-in")); // self-loop
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("loop", error.ToLowerInvariant());
    }

    private static BuiltinFunctionNode MakeNode(string id, string name)
    {
        var n = new BuiltinFunctionNode { Id = id, Name = name, FunctionName = name, NodeType = BlueprintNodeType.BuiltinFunction };
        n.InputPins.Add(new BlueprintPin { Id = $"{id}-in", Name = "Exec", Direction = PinDirection.Input });
        n.OutputPins.Add(new BlueprintPin { Id = $"{id}-out", Name = "Exec", Direction = PinDirection.Output });
        return n;
    }

    private static BlueprintConnection Conn(string srcNode, string srcPin, string tgtNode, string tgtPin)
        => new() { Id = Guid.NewGuid().ToString(), SourceNodeId = srcNode, SourcePinId = srcPin, TargetNodeId = tgtNode, TargetPinId = tgtPin };
}