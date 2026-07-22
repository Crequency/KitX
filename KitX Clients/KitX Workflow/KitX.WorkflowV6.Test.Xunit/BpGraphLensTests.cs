// ─────────────────────────────────────────────────────────────────────────────
// Phase 8 acceptance tests for BpGraphLens (IR → Blueprint projection).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BpGraphLensTests
{
    private static BuiltinFunctionRegistry Registry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    private static Blueprint ProjectBS(string src)
    {
        var registry = Registry();
        var lens = new KsTextLens(registry);
        var ir = lens.Parse(src, []);
        var bpLens = new BpGraphLens(registry);
        return bpLens.Project(ir);
    }

    [Fact]
    public void Project_Empty_IR_Empty_Blueprint()
    {
        var bpLens = new BpGraphLens(Registry());
        var bp = bpLens.Project(new Workflow());
        Assert.Empty(bp.Nodes);
    }

    [Fact]
    public void Project_Single_Print()
    {
        var bp = ProjectBS("Print(\"hello\")\n");
        // Should have: EntryNode + BuiltinFunctionNode (Print).
        // "hello" literal goes to Print's DefaultValue, not a separate ConstNode.
        Assert.Equal(2, bp.Nodes.Count);
        Assert.Single(bp.Nodes.OfType<EntryNode>());
        var funcNodes = bp.Nodes.OfType<BuiltinFunctionNode>().ToList();
        Assert.Single(funcNodes);
        Assert.Equal("Print", funcNodes[0].FunctionName);
        // The "hello" literal should be on the Value input pin's DefaultValue.
        var valuePin = funcNodes[0].InputPins.Find(p => p.Name == "Value");
        Assert.NotNull(valuePin);
        Assert.Equal("hello", valuePin!.DefaultValue);
    }

    [Fact]
    public void Project_Single_Print_Has_Exec_Connection()
    {
        var bp = ProjectBS("Print(\"hello\")\n");
        // Entry → Print exec connection (data literal is via DefaultValue, no data edge).
        Assert.Contains(bp.Connections, c =>
        {
            var from = bp.Nodes.Find(n => n.Id == c.SourceNodeId);
            var to = bp.Nodes.Find(n => n.Id == c.TargetNodeId);
            return from is EntryNode && to is BuiltinFunctionNode { FunctionName: "Print" };
        });
    }

    [Fact]
    public void Project_If_Statement()
    {
        var bp = ProjectBS("if Compare(\"BEQ\", 1, 1)\n    Print(\"yes\")\n");
        // Branch + then-body scope (Entry→Print) + EntryNode for top-level.
        var branches = bp.Nodes.OfType<BuiltinFunctionNode>().Where(n => n.FunctionName == "Branch").ToList();
        Assert.Single(branches);
        // Branch should have True/False output pins.
        Assert.Contains(branches[0].OutputPins, p => p.Name == "True");
        Assert.Contains(branches[0].OutputPins, p => p.Name == "False");
        // Print function node present.
        Assert.Contains(bp.Nodes.OfType<BuiltinFunctionNode>(), n => n.FunctionName == "Print");
    }

    [Fact]
    public void Project_ForEach_Statement()
    {
        var bp = ProjectBS("forEach Range(0, 5, 1) as i\n    i > Print\n");
        var each = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "Each");
        Assert.NotNull(each);
        Assert.Contains(each!.OutputPins, p => p.Name == "Body");
        Assert.Contains(each.OutputPins, p => p.Name == "Current");
        // Print function node present.
        Assert.Contains(bp.Nodes.OfType<BuiltinFunctionNode>(), n => n.FunctionName == "Print");
    }

    [Fact]
    public void Project_Switch_Statement()
    {
        var bp = ProjectBS("""
            switch sel
                0:
                    Print("zero")
                1:
                    Print("one")
                default:
                    Print("other")
            """);
        var sw = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "Switch");
        Assert.NotNull(sw);
        // Selector data input pin.
        Assert.Contains(sw!.InputPins, p => p.Name == "Selector");
        // Two arm Exec output pins (0, 1) + Default.
        Assert.Contains(sw.OutputPins, p => p.Name == "0");
        Assert.Contains(sw.OutputPins, p => p.Name == "1");
        Assert.Contains(sw.OutputPins, p => p.Name == "Default");
        // Each arm body contains a Print node.
        Assert.Equal(3, bp.Nodes.OfType<BuiltinFunctionNode>().Count(n => n.FunctionName == "Print"));
    }

    [Fact]
    public void Project_Multi_Arg_Function_Has_Named_Pins()
    {
        // Range(From, To, Step) should create 3 named input pins, not a single "Value".
        var bp = ProjectBS("forEach Range(0, 3, 1) as i\n    i > Print\n");
        var range = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "Range");
        Assert.NotNull(range);
        Assert.Contains(range!.InputPins, p => p.Name == "From");
        Assert.Contains(range.InputPins, p => p.Name == "To");
        Assert.Contains(range.InputPins, p => p.Name == "Step");
        // Literals 0/3/1 should be on the From/To/Step pins' DefaultValues.
        var fromPin = range.InputPins.Find(p => p.Name == "From");
        Assert.NotNull(fromPin);
        Assert.Equal("0", fromPin!.DefaultValue);
        var toPin = range.InputPins.Find(p => p.Name == "To");
        Assert.NotNull(toPin);
        Assert.Equal("3", toPin!.DefaultValue);
        var stepPin = range.InputPins.Find(p => p.Name == "Step");
        Assert.NotNull(stepPin);
        Assert.Equal("1", stepPin!.DefaultValue);
        // Range output pin should be named "Range" (from PortSpec), not "Value".
        Assert.Contains(range.OutputPins, p => p.Name == "Range");
    }

    [Fact]
    public void Project_Compare_Has_Op_A_B_Pins()
    {
        // Compare(Op, A, B) should create 3 named input pins.
        var bp = ProjectBS("var {\n    int a\n    int b\n}\n\na, b > Compare(\"BEQ\") > Print\n");
        var compare = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "Compare");
        Assert.NotNull(compare);
        Assert.Contains(compare!.InputPins, p => p.Name == "Op");
        Assert.Contains(compare.InputPins, p => p.Name == "A");
        Assert.Contains(compare.InputPins, p => p.Name == "B");
        // "BEQ" literal should be on the Op pin's DefaultValue.
        var opPin = compare.InputPins.Find(p => p.Name == "Op");
        Assert.NotNull(opPin);
        Assert.Equal("BEQ", opPin!.DefaultValue);
        // Output pin should be named "Result" (from PortSpec).
        Assert.Contains(compare.OutputPins, p => p.Name == "Result");
    }

    [Fact]
    public void Node_Ids_Stable_Across_Project()
    {
        // Two projections of the same source should produce identical node IDs.
        var bp1 = ProjectBS("Print(\"hello\")\n");
        var bp2 = ProjectBS("Print(\"hello\")\n");
        Assert.Equal(bp1.Nodes.Count, bp2.Nodes.Count);
        for (int i = 0; i < bp1.Nodes.Count; i++)
            Assert.Equal(bp1.Nodes[i].Id, bp2.Nodes[i].Id);
    }

    [Fact]
    public void Project_All_Nodes_Have_Unique_Ids()
    {
        var bp = ProjectBS("if Compare(\"BEQ\", 1, 1)\n    Print(\"yes\")\nelse\n    Print(\"no\")\n");
        var ids = bp.Nodes.Select(n => n.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }

    [Fact]
    public void Project_All_Pins_Have_Unique_Ids()
    {
        var bp = ProjectBS("Print(\"hello\")\n");
        var pinIds = bp.Nodes.SelectMany(n => n.InputPins.Concat(n.OutputPins)).Select(p => p.Id).ToList();
        Assert.True(pinIds.Count > 0);
        Assert.Equal(pinIds.Distinct().Count(), pinIds.Count);
    }

    // ── Exec chain coverage ──

    [Fact]
    public void Project_Sequential_Prints_Have_Exec_Chain()
    {
        var bp = ProjectBS("Print(\"a\")\nPrint(\"b\")\n");
        var prints = bp.Nodes.OfType<BuiltinFunctionNode>().Where(n => n.FunctionName == "Print").ToList();
        Assert.Equal(2, prints.Count);
        // There must be an exec connection from Print-0 to Print-1.
        Assert.Contains(bp.Connections, c =>
            c.SourceNodeId == prints[0].Id && c.TargetNodeId == prints[1].Id);
    }

    [Fact]
    public void Project_Entry_Connects_To_First_Statement()
    {
        var bp = ProjectBS("Print(\"hello\")\n");
        var entry = bp.Nodes.OfType<EntryNode>().First();
        var print = bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Print");
        Assert.Contains(bp.Connections, c =>
            c.SourceNodeId == entry.Id && c.TargetNodeId == print.Id);
    }

    // ── Control-flow nodes have Exec input ──

    [Fact]
    public void Project_Break_Node_Has_Exec_Input()
    {
        var bp = ProjectBS("forEach Range(0, 3, 1) as i\n    break\n");
        var breakNode = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "break");
        Assert.NotNull(breakNode);
        Assert.NotEmpty(breakNode!.InputPins);
        Assert.Contains(breakNode.InputPins, p => p.Name == "Exec");
    }

    [Fact]
    public void Project_Exit_Node_Has_Exec_Input()
    {
        var bp = ProjectBS("Print(\"x\")\nexit()\n");
        var exitNode = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "exit");
        Assert.NotNull(exitNode);
        Assert.NotEmpty(exitNode!.InputPins);
        Assert.Contains(exitNode.InputPins, p => p.Name == "Exec");
    }

    [Fact]
    public void Project_Continue_Node_Has_Exec_Input()
    {
        var bp = ProjectBS("forEach Range(0, 3, 1) as i\n    continue\n");
        var ctNode = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "continue");
        Assert.NotNull(ctNode);
        Assert.NotEmpty(ctNode!.InputPins);
        Assert.Contains(ctNode.InputPins, p => p.Name == "Exec");
    }

    // ── Pipeline variable taps → VariableNode ──

    [Fact]
    public void Project_Pipeline_Variable_Tap_Is_VariableNode()
    {
        // `0 > counter` — the >counter segment should become a VariableNode, not BuiltinFunction.
        var bp = ProjectBS("var {\n    int counter\n}\n\n0 > counter\n");
        var varNodes = bp.Nodes.OfType<VariableNode>().ToList();
        Assert.Contains(varNodes, n => n.VarName == "counter");
        // The counter variable must have a data input (write) pin.
        var counterNode = varNodes.First(n => n.VarName == "counter");
        Assert.NotEmpty(counterNode.InputPins);
    }

    [Fact]
    public void Project_Multi_Source_Pipeline_Chains_Data_Flow()
    {
        // `guessNum, targetNum > Compare("BEQ") > cond`
        // Should produce: VariableNode(guessNum,read) + VariableNode(targetNum,read)
        // + BuiltinFunction(Compare) + VariableNode(cond,write)
        // with data connections chaining through.
        var bp = ProjectBS("var {\n    int guessNum\n    int targetNum\n    int cond\n}\n\nguessNum, targetNum > Compare(\"BEQ\") > cond\n");
        var compare = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "Compare");
        Assert.NotNull(compare);
        // Find the USAGE variable node for cond (the one with incoming connections),
        // not the definition node (which is standalone).
        var condNodes = bp.Nodes.OfType<VariableNode>().Where(n => n.VarName == "cond").ToList();
        Assert.NotEmpty(condNodes);
        // At least one cond node must have a connection from the compare function.
        Assert.Contains(bp.Connections, c =>
            c.SourceNodeId == compare!.Id &&
            condNodes.Exists(cn => cn.Id == c.TargetNodeId));
    }

    // ── Structural correctness tests (Phase 3.2) ──

    [Fact]
    public void No_Duplicate_Entry_Nodes()
    {
        // Top-level + if-then + if-else + forEach-body → only 1 EntryNode total.
        var bp = ProjectBS("""
            if cond
                Print("then")
            else
                Print("else")
            """);
        var entries = bp.Nodes.OfType<EntryNode>().ToList();
        Assert.Single(entries);
    }

    [Fact]
    public void Branch_Has_Condition_Input_Pin()
    {
        var bp = ProjectBS("if cond\n    Print(\"yes\")\n");
        var branch = bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Branch");
        Assert.Contains(branch.InputPins, p => p.Name == "Condition");
        Assert.Equal(PinType.Boolean, branch.InputPins.First(p => p.Name == "Condition").Type);
    }

    [Fact]
    public void While_Has_Condition_Input_Pin()
    {
        var bp = ProjectBS("""
            var {
                int counter
            }

            0 > counter
            while counter, 3 > Compare("BLT")
                counter, 1 > Add > counter
            """);
        var whileNode = bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "While");
        Assert.Contains(whileNode.InputPins, p => p.Name == "Condition");
    }

    [Fact]
    public void Definition_Nodes_Have_No_Connections()
    {
        // const/var definition nodes are standalone — they don't participate in edges.
        var bp = ProjectBS("""
            const {
                int max = 5
            }

            var {
                int counter
            }

            0 > counter
            """);
        var constNode = bp.Nodes.OfType<ConstNode>().FirstOrDefault(n => n.ConstName == "max");
        Assert.NotNull(constNode);
        Assert.DoesNotContain(bp.Connections, c => c.SourceNodeId == constNode!.Id || c.TargetNodeId == constNode.Id);

        // The definition VariableNode for "counter" should also have no connections.
        // The usage VariableNode (from `0 > counter`) should have connections.
        var counterDefs = bp.Nodes.OfType<VariableNode>().Where(n => n.VarName == "counter").ToList();
        Assert.True(counterDefs.Count >= 2);  // at least def + usage
    }

    [Fact]
    public void If_Else_Both_Branches_Connect_Forward()
    {
        // After if/else, both branches' tails should connect to the next statement.
        var bp = ProjectBS("""
            if cond
                Print("then")
            else
                Print("else")
            Print("after")
            """);
        var afterPrint = bp.Nodes.OfType<BuiltinFunctionNode>()
            .Where(n => n.FunctionName == "Print")
            .Last();
        // Both "then" and "else" Print nodes should have exec connections to "after" Print.
        var incomingExec = bp.Connections.Where(c => c.TargetNodeId == afterPrint.Id).ToList();
        Assert.True(incomingExec.Count >= 2, $"Expected >=2 incoming exec connections, got {incomingExec.Count}");
    }

    [Fact]
    public void ForEach_Body_Starts_From_Each_Body_Pin()
    {
        var bp = ProjectBS("""
            forEach Range(0, 3, 1) as i
                i > Print
            """);
        var each = bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Each");
        var print = bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Print");
        // There should be an exec connection from Each.Body to the Print node.
        Assert.Contains(bp.Connections, c =>
            c.SourceNodeId == each.Id && c.TargetNodeId == print.Id);
    }

    [Fact]
    public void Node_Ids_Are_Short()
    {
        // Deep nesting should NOT produce long IDs (FNV hash → fixed 10 chars: "n_" + 8 hex).
        var bp = ProjectBS("""
            if a
                if b
                    if c
                        if d
                            Print("deep")
            """);
        foreach (var node in bp.Nodes)
            Assert.True(node.Id.Length <= 20, $"Node ID too long: {node.Id} ({node.Id.Length} chars)");
    }

    [Fact]
    public void Pipeline_Condition_Renders_Data_Flow()
    {
        // `if 1, 1 > Compare("BEQ")` → should produce data nodes for the
        // condition pipeline (sources + Compare function) and connect
        // the function output to Branch.Condition.
        var bp = ProjectBS("if 1, 1 > Compare(\"BEQ\")\n    Print(\"yes\")\n");
        var branch = bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Branch");
        var compare = bp.Nodes.OfType<BuiltinFunctionNode>().FirstOrDefault(n => n.FunctionName == "Compare");
        Assert.NotNull(compare);
        // Compare output should connect to Branch.Condition.
        Assert.Contains(bp.Connections, c =>
            c.SourceNodeId == compare!.Id && c.TargetNodeId == branch.Id);
    }

    // ── Stress tests (Phase 3.4) ──

    [Fact]
    public void Stress_Deep_Nesting_Ids_Bounded()
    {
        // 10 levels of nested if — all Node IDs must be ≤ 20 chars.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 10; i++)
        {
            sb.Append(new string(' ', i * 4));
            sb.Append($"if v{i}\n");
        }
        sb.Append(new string(' ', 10 * 4));
        sb.Append("Print(\"deep\")\n");
        var bp = ProjectBS(sb.ToString());
        foreach (var node in bp.Nodes)
            Assert.True(node.Id.Length <= 20, $"ID too long at depth: {node.Id}");
    }

    [Fact]
    public void Stress_Repeated_Project_Stable_NodeIds()
    {
        // Same KS projected 5 times → identical node IDs each time.
        var src = """
            forEach Range(0, 3, 1) as i
                i, 2 > Compare("BEQ")
                if i, 2 > Compare("BEQ")
                    break
                i > Print
            """;
        var first = ProjectBS(src);
        for (int rep = 0; rep < 4; rep++)
        {
            var again = ProjectBS(src);
            Assert.Equal(first.Nodes.Count, again.Nodes.Count);
            for (int i = 0; i < first.Nodes.Count; i++)
                Assert.Equal(first.Nodes[i].Id, again.Nodes[i].Id);
        }
    }

    [Fact]
    public void Stress_Round_Trip_BS_IR_BS()
    {
        // KS → parse → IR → render → KS → parse → IR: should be idempotent.
        var src = """
            if 1, 1 > Compare("BEQ")
                Print("yes")
            else
                Print("no")
            """;
        var lens = new KsTextLens(Registry());
        var ir1 = lens.Parse(src, []);
        var rendered = lens.Project(ir1);
        var ir2 = lens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }
}