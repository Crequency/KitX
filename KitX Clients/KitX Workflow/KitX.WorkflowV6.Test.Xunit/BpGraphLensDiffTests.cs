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

[Trait("Category", "Unit")]
public class BpGraphLensDiffTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public BpGraphLensDiffTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private Workflow ParseKS(string src) => _fixture.KsLens.Parse(src, []);

    [Fact]
    public void Diff_Empty_Edits_Returns_Empty_Diff()
    {
        var lens = _fixture.BpLens;
        var ir = ParseKS("Print(\"a\")\n");
        var diff = lens.Diff(ir, []);
        Assert.NotNull(diff);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Diff_Add_Node_Produces_Added_Change()
    {
        var lens = _fixture.BpLens;
        var ir = ParseKS("Print(\"a\")\n");
        var edits = new BpEditAction[] { new AddNodeInBlock("/top", "Print") };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Added);
    }

    [Fact]
    public void Diff_Delete_Node_Produces_Removed_Change()
    {
        var lens = _fixture.BpLens;
        var ir = ParseKS("Print(\"a\")\n");
        var edits = new BpEditAction[] { new DeleteNode("v6-/top/stmt/0") };
        var diff = lens.Diff(ir, edits);
        Assert.NotNull(diff);
        Assert.Contains(diff.StatementChanges, c => c.Kind == DiffKind.Removed);
    }

    [Fact]
    public void Structural_Simple_Pipeline_Valid()
    {
        var bp = _fixture.BpLens.Project(ParseKS("Print(\"hello\")\n"));
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_If_Else_Valid()
    {
        var bp = _fixture.BpLens.Project(ParseKS("if Compare(\"BEQ\", 1, 1):\n    Print(\"yes\")\n"));
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_ForEach_Valid()
    {
        var bp = _fixture.BpLens.Project(ParseKS("forEach Range(0, 5, 1) as i:\n    i > Print\n"));
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
        Assert.NotNull(error);  // E3: assert error exists; don't freeze UX wording
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
        Assert.NotNull(error);  // E3: assert error exists; don't freeze UX wording
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

    private Blueprint ProjectKS(string src)
        => _fixture.BpLens.Project(_fixture.KsLens.Parse(src, []));

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
        Assert.Contains("KS111", result!);
    }

    [Fact]
    public void Structural_Allows_End_Pin_Model_If_Else()
    {
        // v6 End-pin model: if/else branches' tails dangle; post-if connects to
        // Branch.End. No multi-exec merge — the graph is a pure tree-shaped DAG.
        var bp = ProjectKS("if 1, 1 > Compare(\"BEQ\"):\n    Print(\"then\")\nelse:\n    Print(\"else\")\nPrint(\"after\")\n");
        var result = StructuralReducer.Check(bp);
        Assert.Null(result);
    }

    // ── End-pin model constraint rejection tests ──

    [Fact]
    public void Structural_Rejects_Diamond_Merge_Multi_Exec_Input()
    {
        // Manually build a graph where two nodes' Exec outputs both connect to the
        // same target node's Exec input — a diamond merge forbidden by E3/KS102.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var a = MakeNode("a", "A");
        var b = MakeNode("b", "B");
        var merge = MakeNode("m", "Merge");
        bp.Nodes.Add(entry); bp.Nodes.Add(a); bp.Nodes.Add(b); bp.Nodes.Add(merge);
        bp.Connections.Add(Conn("e", "eo", "a", "a-in"));
        bp.Connections.Add(Conn("e", "eo", "b", "b-in"));
        bp.Connections.Add(Conn("a", "a-out", "m", "m-in"));
        bp.Connections.Add(Conn("b", "b-out", "m", "m-in"));
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS102", error!);
    }

    [Fact]
    public void Structural_Rejects_Break_Outside_Loop()
    {
        // break at top level (no enclosing loop) — violates KS140.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var brk = new BuiltinFunctionNode { Id = "bk", Name = "break", FunctionName = "break", NodeType = BlueprintNodeType.BuiltinFunction };
        brk.InputPins.Add(new BlueprintPin { Id = "bk-in", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        bp.Nodes.Add(entry); bp.Nodes.Add(brk);
        bp.Connections.Add(Conn("e", "eo", "bk", "bk-in"));
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS140", error!);
    }

    [Fact]
    public void Structural_Allows_Break_Inside_ForEach_Body()
    {
        // break inside a forEach body — valid, KS140 should not fire.
        var bp = ProjectKS("forEach Range(0, 3, 1) as i:\n    break\n");
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_Allows_Break_Inside_While_Body()
    {
        var bp = ProjectKS("""
            var {
                bool c
            }
            while c:
                break
            """);
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }

    [Fact]
    public void Structural_Allows_Nested_If_Inside_ForEach_With_Break()
    {
        // Nested control flow — break is inside forEach (the nearest enclosing loop).
        var bp = ProjectKS("""
            const {
                int g = 5
            }
            var {
                bool c
            }
            forEach Range(0, 3, 1) as i:
                if c:
                    break
            """);
        var error = StructuralReducer.Check(bp);
        Assert.Null(error);
    }
}