// ─────────────────────────────────────────────────────────────────────────────
// Phase 8 acceptance tests for BpGraphLens (IR → Blueprint projection).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.BsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BpGraphLensTests
{
    private static BuiltinFunctionRegistry Registry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    private static Blueprint ProjectBS(string src)
    {
        var registry = Registry();
        var lens = new BsTextLens(registry);
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
        // Should have: EntryNode + BuiltinFunctionNode (Print) + ConstNode ("hello") + ExitPointNode.
        Assert.Equal(4, bp.Nodes.Count);
        Assert.Single(bp.Nodes.OfType<EntryNode>());
        Assert.Single(bp.Nodes.OfType<ExitPointNode>());
        var funcNodes = bp.Nodes.OfType<BuiltinFunctionNode>().ToList();
        Assert.Single(funcNodes);
        Assert.Equal("Print", funcNodes[0].FunctionName);
        Assert.Single(bp.Nodes.OfType<ConstNode>());
    }

    [Fact]
    public void Project_Single_Print_Has_Connections()
    {
        var bp = ProjectBS("Print(\"hello\")\n");
        Assert.NotEmpty(bp.Connections);
        // ConstNode Value → BuiltinFunctionNode Value connection.
        Assert.Contains(bp.Connections, c =>
        {
            var from = bp.Nodes.Find(n => n.Id == c.SourceNodeId);
            var to = bp.Nodes.Find(n => n.Id == c.TargetNodeId);
            return from is ConstNode && to is BuiltinFunctionNode { FunctionName: "Print" };
        });
    }

    [Fact]
    public void Project_If_Statement()
    {
        var bp = ProjectBS("if HelperFuncCompare(\"BEQ\", 1, 1)\n    Print(\"yes\")\n");
        // Branch + then-body scope (Entry→Print→Exit) + EntryNode/ExitPointNode for top-level.
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
        var bp = ProjectBS("if HelperFuncCompare(\"BEQ\", 1, 1)\n    Print(\"yes\")\nelse\n    Print(\"no\")\n");
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
}