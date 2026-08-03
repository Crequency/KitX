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
        Assert.Contains("KS105", error);  // explicit exec back-edge → KS105 (E6)
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
        // The self-loop makes the node's Exec input have 2 incoming edges — E3/KS102
        // fires before the E6/KS105 cycle check (check order is by design).
        Assert.Contains("KS102", error);
    }

    // ── Per-code constraint tests (KS100-KS140 coverage) ──

    [Fact]
    public void Structural_Rejects_Isolated_Node_KS100()
    {
        // A non-definition node with no exec/data path from Entry violates E1 connectivity.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var linked = MakeNode("l", "Linked");
        var orphan = MakeNode("o", "Orphan");
        bp.Nodes.Add(entry); bp.Nodes.Add(linked); bp.Nodes.Add(orphan);
        bp.Connections.Add(Conn("e", "eo", "l", "l-in"));
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS100", error);
    }

    [Fact]
    public void Structural_Rejects_Explicit_Exec_Back_Edge_KS105()
    {
        // Explicit exec cycle (Entry → A → B → Entry) must be reported as KS105 (E6),
        // not merely as a generic error.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var a = MakeNode("a", "A");
        var b = MakeNode("b", "B");
        bp.Nodes.Add(entry); bp.Nodes.Add(a); bp.Nodes.Add(b);
        bp.Connections.Add(Conn("e", "eo", "a", "a-in"));
        bp.Connections.Add(Conn("a", "a-out", "b", "b-in"));
        bp.Connections.Add(Conn("b", "b-out", "e", "eo"));  // back to Entry's exec out
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS105", error);
    }

    [Fact]
    public void Structural_Rejects_Data_Cycle_KS110()
    {
        // A data-edge cycle (values depending on themselves) violates D1/DAG → KS110.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var a = MakeNode("a", "A");
        var b = MakeNode("b", "B");
        a.InputPins.Add(new BlueprintPin { Id = "a-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        a.OutputPins.Add(new BlueprintPin { Id = "a-vout", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });
        b.InputPins.Add(new BlueprintPin { Id = "b-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        b.OutputPins.Add(new BlueprintPin { Id = "b-vout", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });
        bp.Nodes.Add(entry); bp.Nodes.Add(a); bp.Nodes.Add(b);
        bp.Connections.Add(Conn("e", "eo", "a", "a-in"));
        bp.Connections.Add(Conn("a", "a-out", "b", "b-in"));
        bp.Connections.Add(Conn("a", "a-vout", "b", "b-vin"));
        bp.Connections.Add(Conn("b", "b-vout", "a", "a-vin"));  // data cycle: a → b → a
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS110", error);
    }

    [Fact]
    public void Structural_Rejects_Usage_Node_Without_Exec_Pin_KS120()
    {
        // A non-definition node with no Exec pins is data-reachable (it feeds an
        // exec-reachable consumer), so KS100 does not fire — but C1 demands Exec pins
        // on every non-definition node → KS120.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var a = MakeNode("a", "A");
        a.InputPins.Add(new BlueprintPin { Id = "a-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        var noExec = new BuiltinFunctionNode { Id = "ne", Name = "NoExec", FunctionName = "NoExec", NodeType = BlueprintNodeType.BuiltinFunction };
        noExec.OutputPins.Add(new BlueprintPin { Id = "ne-vout", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });
        bp.Nodes.Add(entry); bp.Nodes.Add(a); bp.Nodes.Add(noExec);
        bp.Connections.Add(Conn("e", "eo", "a", "a-in"));
        bp.Connections.Add(Conn("ne", "ne-vout", "a", "a-vin"));  // noExec feeds a's data input
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS120", error);
    }

    [Fact]
    public void Structural_Rejects_Unmatched_VarName_KS130()
    {
        // A usage VariableNode whose VarName has no matching definition node violates N2 → KS130.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var a = MakeNode("a", "A");
        var usage = new VariableNode { Id = "v", Name = "ghost", VarName = "ghost", VarKind = VariableKind.PubVar, NodeType = BlueprintNodeType.Variable };
        usage.InputPins.Add(new BlueprintPin { Id = "v-in", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        usage.OutputPins.Add(new BlueprintPin { Id = "v-out", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        usage.InputPins.Add(new BlueprintPin { Id = "v-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        usage.OutputPins.Add(new BlueprintPin { Id = "v-vout", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });
        bp.Nodes.Add(entry); bp.Nodes.Add(a); bp.Nodes.Add(usage);
        bp.Connections.Add(Conn("e", "eo", "a", "a-in"));
        bp.Connections.Add(Conn("a", "a-out", "v", "v-in"));
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS130", error);
    }

    [Fact]
    public void Structural_Rejects_Multi_Path_Access_KS101()
    {
        // A node reachable only via a NON-"Exec"-named exec pin: the KS100 BFS follows
        // every exec-typed pin (so connectivity passes), but the structured walk only
        // follows pins named "Exec" — the node is never visited → KS101 (E2) fires.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var a = MakeNode("a", "A");
        a.OutputPins.Clear();  // drop the standard "Exec" out; expose a non-standard exec pin
        a.OutputPins.Add(new BlueprintPin { Id = "a-cout", Name = "CustomExec", Direction = PinDirection.Output, Type = PinType.Execution });
        var b = MakeNode("b", "B");
        bp.Nodes.Add(entry); bp.Nodes.Add(a); bp.Nodes.Add(b);
        bp.Connections.Add(Conn("e", "eo", "a", "a-in"));
        bp.Connections.Add(Conn("a", "a-cout", "b", "b-in"));
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS101", error);
    }

    private static BuiltinFunctionNode MakeNode(string id, string name)
    {
        var n = new BuiltinFunctionNode { Id = id, Name = name, FunctionName = name, NodeType = BlueprintNodeType.BuiltinFunction };
        n.InputPins.Add(new BlueprintPin { Id = $"{id}-in", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        n.OutputPins.Add(new BlueprintPin { Id = $"{id}-out", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        return n;
    }

    private static BuiltinFunctionNode MakeBranch(string id)
    {
        var n = new BuiltinFunctionNode { Id = id, Name = "Branch", FunctionName = "Branch", NodeType = BlueprintNodeType.BuiltinFunction };
        n.InputPins.Add(new BlueprintPin { Id = $"{id}-in", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        n.InputPins.Add(new BlueprintPin { Id = $"{id}-cond", Name = "Condition", Direction = PinDirection.Input, Type = PinType.Boolean });
        n.OutputPins.Add(new BlueprintPin { Id = $"{id}-true", Name = "True", Direction = PinDirection.Output, Type = PinType.Execution });
        n.OutputPins.Add(new BlueprintPin { Id = $"{id}-false", Name = "False", Direction = PinDirection.Output, Type = PinType.Execution });
        n.OutputPins.Add(new BlueprintPin { Id = $"{id}-end", Name = "End", Direction = PinDirection.Output, Type = PinType.Execution });
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

    // ── D3 (KS112) / D4 (KS113) scope constraint tests ──

    [Fact]
    public void Structural_Rejects_Cross_Scope_Data_Edge_KS112()
    {
        // A node inside the Branch's then-body feeds a top-level node via a data edge:
        // the source lives in an INNER scope while the consumer is in the OUTER scope
        // → D3 violation. The graph is otherwise well-formed (no KS101/KS102/KS100...).
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var branch = MakeBranch("br");
        var a = MakeNode("a", "A");
        a.InputPins.Add(new BlueprintPin { Id = "a-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        a.OutputPins.Add(new BlueprintPin { Id = "a-vout", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });
        var b = MakeNode("b", "B");
        b.InputPins.Add(new BlueprintPin { Id = "b-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        bp.Nodes.Add(entry); bp.Nodes.Add(branch); bp.Nodes.Add(a); bp.Nodes.Add(b);
        bp.Connections.Add(Conn("e", "eo", "br", "br-in"));
        bp.Connections.Add(Conn("br", "br-true", "a", "a-in"));
        bp.Connections.Add(Conn("br", "br-end", "b", "b-in"));
        bp.Connections.Add(Conn("a", "a-vout", "b", "b-vin"));  // then-body → top-level data edge
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS112", error!);
    }

    [Fact]
    public void Structural_Rejects_Condition_Subgraph_In_Body_KS113()
    {
        // A node inside br1's then-body feeds br2's Condition data pin (br2 sits at
        // top level): the condition sub-graph node lives in the body scope, not in the
        // control-flow node's scope → D4 violation.
        // NOTE: feeding the branch's OWN body would first trip D1/KS110 (an exec+data
        // mixed cycle br→body→br), so the condition source lives in a sibling branch's
        // body — which is exactly the "condition sub-graph leaks into another scope"
        // shape D4 guards against.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var br1 = MakeBranch("br1");
        var br2 = MakeBranch("br2");
        var a = MakeNode("a", "A");
        a.InputPins.Add(new BlueprintPin { Id = "a-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        a.OutputPins.Add(new BlueprintPin { Id = "a-vout", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });
        bp.Nodes.Add(entry); bp.Nodes.Add(br1); bp.Nodes.Add(a); bp.Nodes.Add(br2);
        bp.Connections.Add(Conn("e", "eo", "br1", "br1-in"));
        bp.Connections.Add(Conn("br1", "br1-true", "a", "a-in"));
        bp.Connections.Add(Conn("br1", "br1-end", "br2", "br2-in"));
        bp.Connections.Add(Conn("a", "a-vout", "br2", "br2-cond"));  // body node feeds br2's condition
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS113", error!);
    }

    [Fact]
    public void Structural_Rejects_Cross_Branch_Data_Edge_KS112()
    {
        // A then-body node feeds an else-body node via a data edge: sibling scopes
        // (neither is an ancestor of the other) → D3 violation.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var branch = MakeBranch("br");
        var a = MakeNode("a", "A");
        a.InputPins.Add(new BlueprintPin { Id = "a-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        a.OutputPins.Add(new BlueprintPin { Id = "a-vout", Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });
        var c = MakeNode("c", "C");
        c.InputPins.Add(new BlueprintPin { Id = "c-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        bp.Nodes.Add(entry); bp.Nodes.Add(branch); bp.Nodes.Add(a); bp.Nodes.Add(c);
        bp.Connections.Add(Conn("e", "eo", "br", "br-in"));
        bp.Connections.Add(Conn("br", "br-true", "a", "a-in"));
        bp.Connections.Add(Conn("br", "br-false", "c", "c-in"));
        bp.Connections.Add(Conn("a", "a-vout", "c", "c-vin"));  // then-body → else-body data edge
        var error = StructuralReducer.Check(bp);
        Assert.NotNull(error);
        Assert.Contains("KS112", error!);
    }

    [Fact]
    public void Structural_Allows_Outer_Scope_Data_Edge_KS112()
    {
        // Each.Current (outer scope) feeds a node inside the loop body: an outer→inner
        // data edge is legal per D3. The body item VariableNode is declared by the
        // Each's ItemName property, so KS130 also stays satisfied.
        var bp = new Blueprint();
        var entry = new EntryNode { Id = "e", Name = "Entry", NodeType = BlueprintNodeType.Entry };
        entry.OutputPins.Add(new BlueprintPin { Id = "eo", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        var each = new BuiltinFunctionNode { Id = "each", Name = "Each", FunctionName = "Each", NodeType = BlueprintNodeType.BuiltinFunction };
        each.Properties["ItemName"] = "i";
        each.InputPins.Add(new BlueprintPin { Id = "each-in", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        each.InputPins.Add(new BlueprintPin { Id = "each-list", Name = "List", Direction = PinDirection.Input, Type = PinType.Any });
        each.OutputPins.Add(new BlueprintPin { Id = "each-body", Name = "Body", Direction = PinDirection.Output, Type = PinType.Execution });
        each.OutputPins.Add(new BlueprintPin { Id = "each-end", Name = "End", Direction = PinDirection.Output, Type = PinType.Execution });
        each.OutputPins.Add(new BlueprintPin { Id = "each-cur", Name = "Current", Direction = PinDirection.Output, Type = PinType.Any });
        var item = new VariableNode { Id = "it", Name = "i", VarName = "i", VarKind = VariableKind.PubVar, NodeType = BlueprintNodeType.Variable };
        item.InputPins.Add(new BlueprintPin { Id = "it-in", Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        item.OutputPins.Add(new BlueprintPin { Id = "it-out", Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        item.InputPins.Add(new BlueprintPin { Id = "it-vin", Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        bp.Nodes.Add(entry); bp.Nodes.Add(each); bp.Nodes.Add(item);
        bp.Connections.Add(Conn("e", "eo", "each", "each-in"));
        bp.Connections.Add(Conn("each", "each-body", "it", "it-in"));
        bp.Connections.Add(Conn("each", "each-cur", "it", "it-vin"));  // outer → body data edge
        var result = StructuralReducer.Check(bp);
        Assert.Null(result);
    }

    [Fact]
    public void Structural_Allows_Same_Scope_Condition_Subgraph_KS113()
    {
        // Pipeline condition `a, b > Compare("BEQ")`: the condition source nodes are
        // threaded into the exec chain in the SAME scope as the Branch → D4 satisfied.
        var bp = ProjectKS("""
            var {
                bool a
                bool b
            }
            if a, b > Compare("BEQ"):
                Print("yes")
            """);
        var result = StructuralReducer.Check(bp);
        Assert.Null(result);
    }

    [Fact]
    public void Structural_Allows_Nested_If_KS112_KS113()
    {
        // Nested if: the inner condition and Branch share the outer body scope; every
        // data edge is same-scope or outer→inner → both D3 and D4 satisfied.
        var bp = ProjectKS("""
            var {
                bool c
            }
            if c:
                if c:
                    Print("x")
            """);
        var result = StructuralReducer.Check(bp);
        Assert.Null(result);
    }
}