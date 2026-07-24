// ─────────────────────────────────────────────────────────────────────────────
// Phase 9 tests: BpGraphLens.Diff + StructuralReducer.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BpGraphLensDiffTests
{
    private static BpGraphLens Lens() => new(Registry());

    private static BuiltinFunctionRegistry Registry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    private static KsTextLens KsLens() => new(Registry());

    private static Workflow ParseKS(string src) => KsLens().Parse(src, []);

    [Fact]
    public void Diff_Empty_Edits_Returns_Empty_Diff()
    {
        var lens = Lens();
        var ir = ParseKS("Print(\"a\")\n");
        var diff = lens.Diff(ir, []);
        Assert.NotNull(diff);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Diff_Add_Node_Produces_Added_Change()
    {
        var lens = Lens();
        var ir = ParseKS("Print(\"a\")\n");
        var edits = new BpEditAction[] { new AddNodeInBlock("/top", "Print") };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Added);
    }

    [Fact]
    public void Diff_Delete_Node_Produces_Removed_Change()
    {
        var lens = Lens();
        var ir = ParseKS("Print(\"a\")\n");
        var edits = new BpEditAction[] { new DeleteNode("v6-/top/stmt/0") };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Removed);
    }

    [Fact]
    public void Structural_Simple_Pipeline_Valid()
    {
        var bp = new BpGraphLens(Registry()).Project(ParseKS("Print(\"hello\")\n"));
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_If_Else_Valid()
    {
        var bp = new BpGraphLens(Registry()).Project(ParseKS("if Compare(\"BEQ\", 1, 1):\n    Print(\"yes\")\n"));
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_ForEach_Valid()
    {
        var bp = new BpGraphLens(Registry()).Project(ParseKS("forEach Range(0, 5, 1) as i:\n    i > Print\n"));
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_Non_Structural_Back_Edge_Rejected()
    {
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "entry", Name = "Start", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "entry-out", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
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
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var node = MakeNode("n", "N");
        node.InputPins.Add(new BlueprintPin { Id = "ni2", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
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
        n.InputPins.Add(new BlueprintPin { Id = $"{id}-in", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        n.OutputPins.Add(new BlueprintPin { Id = $"{id}-out", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        return n;
    }

    private static BlueprintConnection Conn(string srcNode, string srcPin, string tgtNode, string tgtPin)
        => new() { Id = Guid.NewGuid().ToString(), SourceNodeId = srcNode, SourcePinId = srcPin, TargetNodeId = tgtNode, TargetPinId = tgtPin };

    private static Blueprint ProjectKS(string src)
    {
        var registry = Registry();
        var ksLens = new KsTextLens(registry);
        var ir = ksLens.Parse(src, []);
        return new BpGraphLens(registry).Project(ir);
    }

    private static Blueprint BuildBlueprintWithMultipleDataConnectionsToSamePin()
    {
        var bp = new Blueprint();

        // Two ConstNodes both connecting to the same BuiltinFunctionNode.Print.Value input.
        var const1 = new ConstNode { Id = "const1", Name = "c1", ConstName = "c1", ConstValue = "1" };
        const1.OutputPins.Add(new BlueprintPin { Id = "c1-out", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });

        var const2 = new ConstNode { Id = "const2", Name = "c2", ConstName = "c2", ConstValue = "2" };
        const2.OutputPins.Add(new BlueprintPin { Id = "c2-out", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });

        var print = new BuiltinFunctionNode { Id = "print", Name = "Print", FunctionName = "Print" };
        print.InputPins.Add(new BlueprintPin { Id = "print-exec", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        print.InputPins.Add(new BlueprintPin { Id = "print-value", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });

        bp.Nodes.Add(const1);
        bp.Nodes.Add(const2);
        bp.Nodes.Add(print);

        // Both const1 and const2 connect to print's Value input (violation).
        bp.Connections.Add(Conn("const1", "c1-out", "print", "print-value"));
        bp.Connections.Add(Conn("const2", "c2-out", "print", "print-value"));

        return bp;
    }

    [Fact]
    public void Structural_Rejects_Multiple_Data_Connections_To_Same_Pin()
    {
        var bp = BuildBlueprintWithMultipleDataConnectionsToSamePin();
        var result = StructuralReducer.Check(bp);
        Assert.NotNull(result);
        Assert.Contains("data flow", result!);
    }

    [Fact]
    public void Structural_Allows_Multiple_Exec_Connections_From_Merged_Branches()
    {
        var bp = ProjectKS("if 1, 1 > Compare(\"BEQ\"):\n    Print(\"then\")\nelse:\n    Print(\"else\")\nPrint(\"after\")\n");
        var result = StructuralReducer.Check(bp);
        Assert.Null(result);
    }
}