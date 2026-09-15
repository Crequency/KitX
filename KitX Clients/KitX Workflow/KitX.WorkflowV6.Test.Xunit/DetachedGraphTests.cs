// ─────────────────────────────────────────────────────────────────────────────
// DetachedGraph tests: BP-side "privileged" exec-unreachable sub-graphs.
//
// When a Blueprint contains nodes NOT reachable from the Entry node (the user
// disconnected an exec edge, or drew a sub-graph without wiring it into the main
// chain), the reverse translator must NOT drop them. They are snapshotted into
// Workflow.DetachedGraphs, preserved by Project and the .kcs serializer, and stay
// invisible to KS text (frontend plan §八-附 设计 C).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Serialization;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class DetachedGraphTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public DetachedGraphTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private static BuiltinFunctionNode MakePrint(string id, double x, double y, string? comment = null)
    {
        var fn = new BuiltinFunctionNode
        {
            Id = id,
            Name = "Print",
            FunctionName = "Print",
            X = x,
            Y = y,
            Comment = comment,
        };
        fn.InputPins.Add(new BlueprintPin { Id = Guid.NewGuid().ToString(), Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        fn.InputPins.Add(new BlueprintPin { Id = Guid.NewGuid().ToString(), Name = "Value", Direction = PinDirection.Input, Type = PinType.Any });
        fn.OutputPins.Add(new BlueprintPin { Id = Guid.NewGuid().ToString(), Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        return fn;
    }

    /// <summary>Builds a Blueprint with a main chain (Print("main")) plus a detached
    /// two-node exec component (DetP1 → DetP2) carrying its own exec edge.</summary>
    private (Blueprint Bp, string P1Id) BuildBlueprintWithDetachedComponent()
    {
        var bp = _fixture.BpLens.Project(_fixture.ParseKS("Print(\"main\")\n"));

        var p1 = MakePrint("n_DET_P1", 500, 120, "DETACHED_TAG_P1");
        var p2 = MakePrint("n_DET_P2", 500, 220);
        bp.Nodes.Add(p1);
        bp.Nodes.Add(p2);
        bp.Connections.Add(new BlueprintConnection
        {
            Id = Guid.NewGuid().ToString(),
            SourceNodeId = p1.Id,
            SourcePinId = p1.OutputPins[0].Id,
            TargetNodeId = p2.Id,
            TargetPinId = p2.InputPins[0].Id,
        });
        return (bp, p1.Id);
    }

    [Fact]
    public void Reverse_Collects_Detached_Component_Into_DetachedGraphs()
    {
        var (bp, p1Id) = BuildBlueprintWithDetachedComponent();

        var ir = _fixture.BpLens.Reverse(bp);

        // Main chain unaffected: exactly one statement.
        Assert.Single(ir.Body);

        // The detached component is snapshotted: 2 nodes + 1 internal exec edge.
        var graph = Assert.Single(ir.DetachedGraphs);
        Assert.Equal(2, graph.Nodes.Length);
        Assert.Single(graph.Connections);
        Assert.Contains(graph.Nodes, n => n.Id == p1Id);
        Assert.Contains(graph.Nodes, n => n.Id == "n_DET_P2");
    }

    [Fact]
    public void Project_Reemits_Detached_Component_With_Stored_Coordinates()
    {
        var (bp, p1Id) = BuildBlueprintWithDetachedComponent();
        var ir = _fixture.BpLens.Reverse(bp);

        var bp2 = _fixture.BpLens.Project(ir);

        // Coordinates are preserved verbatim (layout must not re-arrange detached nodes).
        var p1 = Assert.Single(bp2.Nodes, n => n.Id == p1Id);
        Assert.Equal(500, p1.X);
        Assert.Equal(120, p1.Y);
        var p2 = Assert.Single(bp2.Nodes, n => n.Id == "n_DET_P2");
        Assert.Equal(220, p2.Y);
        // The internal edge survives.
        Assert.Contains(bp2.Connections, c => c.SourceNodeId == p1Id && c.TargetNodeId == "n_DET_P2");
        // Node comments survive the snapshot.
        Assert.Equal("DETACHED_TAG_P1", p1.Comment);
    }

    [Fact]
    public void Detached_Content_Is_Invisible_To_KS_Text()
    {
        var (bp, _) = BuildBlueprintWithDetachedComponent();
        var ir = _fixture.BpLens.Reverse(bp);

        var ksText = _fixture.KsLens.Project(ir);

        // The main statement is there; the detached nodes' marker never reaches KS.
        Assert.Contains("main", ksText);
        Assert.DoesNotContain("DETACHED_TAG_P1", ksText);
    }

    [Fact]
    public void DetachedGraphs_Survive_Serializer_RoundTrip()
    {
        var (bp, p1Id) = BuildBlueprintWithDetachedComponent();
        var ir = _fixture.BpLens.Reverse(bp);

        var json = WorkflowSerializer.Serialize(ir);
        var back = WorkflowSerializer.Deserialize(json);

        var graph = Assert.Single(back.DetachedGraphs);
        Assert.Equal(2, graph.Nodes.Length);
        Assert.Contains(graph.Nodes, n => n.Id == p1Id);
        Assert.Single(back.Body);
    }

    [Fact]
    public void Deserialize_Without_DetachedGraphs_Field_Is_Compatible()
    {
        // Old .kcs payloads carry no DetachedGraphs property — must deserialize to empty.
        const string oldJson = """
            {
              "Version": "v6.0",
              "Body": [],
              "Constants": [],
              "GlobalVars": [],
              "HelperFunctions": [],
              "Annotations": []
            }
            """;

        var ir = WorkflowSerializer.Deserialize(oldJson);

        Assert.Empty(ir.DetachedGraphs);
    }

    [Fact]
    public void Detached_Component_RoundTrips_Reverse_Project_Reverse()
    {
        var (bp, p1Id) = BuildBlueprintWithDetachedComponent();
        var ir1 = _fixture.BpLens.Reverse(bp);

        var bp2 = _fixture.BpLens.Project(ir1);
        var ir2 = _fixture.BpLens.Reverse(bp2);

        Assert.Single(ir1.DetachedGraphs);
        Assert.Single(ir2.DetachedGraphs);
        var g1 = ir1.DetachedGraphs[0];
        var g2 = ir2.DetachedGraphs[0];
        Assert.Equal(g1.Nodes.Length, g2.Nodes.Length);
        Assert.Equal(g1.Connections.Length, g2.Connections.Length);
        Assert.Contains(g2.Nodes, n => n.Id == p1Id);
        Assert.Single(ir2.Body);
    }

    [Fact]
    public void Fully_Connected_Graph_Produces_No_DetachedGraphs()
    {
        var bp = _fixture.BpLens.Project(_fixture.ParseKS("""
            Print("a")
            if true:
                Print("yes")
            """));
        var ir = _fixture.BpLens.Reverse(bp);

        Assert.Empty(ir.DetachedGraphs);
    }

    [Fact]
    public void Isolated_Single_Node_Is_Snapshotted()
    {
        var bp = _fixture.BpLens.Project(_fixture.ParseKS("Print(\"main\")\n"));
        var lone = MakePrint("n_DET_LONE", 400, 300);
        bp.Nodes.Add(lone);

        var ir = _fixture.BpLens.Reverse(bp);

        var graph = Assert.Single(ir.DetachedGraphs);
        Assert.Single(graph.Nodes);
        Assert.Equal("n_DET_LONE", graph.Nodes[0].Id);
        Assert.Empty(graph.Connections);
    }

    [Fact]
    public void Definition_Nodes_Are_Not_Collected_As_Detached()
    {
        var bp = _fixture.BpLens.Project(_fixture.ParseKS("""
            const {
                int max = 3
            }
            Print("main")
            """));
        var ir = _fixture.BpLens.Reverse(bp);

        // Definition nodes fold into Constants — never DetachedGraphs.
        Assert.Empty(ir.DetachedGraphs);
        Assert.True(ir.Constants.ContainsKey("max"));
    }

    [Fact]
    public void Detached_Graphs_Survive_KS_Text_RoundTrip_With_Baseline()
    {
        // Symmetry fix (2026-08-04, counterpart of the KS doc-comment privilege): a
        // detached graph is invisible to KS text by design (B1), so a cross-privilege
        // trip through the KS text (BP → KS text → re-parse) drops it — unless the
        // caller re-attaches it from the pre-parse IR via ParseLowering's bpPrivileged
        // parameter (same re-attachment pattern as the doc comments' ksPrivileged).
        var (bp, p1Id) = BuildBlueprintWithDetachedComponent();
        var ir = _fixture.BpLens.Reverse(bp);
        Assert.Single(ir.DetachedGraphs);

        var ksText = _fixture.KsLens.Project(ir);
        var reParsed = _fixture.ParseKS(ir, ksText);
        Assert.Single(reParsed.DetachedGraphs);
        Assert.Contains(reParsed.DetachedGraphs[0].Nodes, n => n.Id == p1Id);

        // The re-projected BP still shows the detached component.
        var bp2 = _fixture.BpLens.Project(reParsed);
        Assert.Contains(bp2.Nodes, n => n.Id == p1Id);
    }

    [Fact]
    public void Detached_Graphs_Dropped_Without_Baseline()
    {
        // Backward compatibility: the no-baseline overload keeps the previous behaviour
        // (detached graphs are lost on a trip through the KS text) — callers must opt
        // in by passing the pre-parse IR.
        var (bp, p1Id) = BuildBlueprintWithDetachedComponent();
        var ir = _fixture.BpLens.Reverse(bp);
        var ksText = _fixture.KsLens.Project(ir);

        var reParsed = _fixture.ParseKS(ksText);
        Assert.Empty(reParsed.DetachedGraphs);
        var bp2 = _fixture.BpLens.Project(reParsed);
        Assert.DoesNotContain(bp2.Nodes, n => n.Id == p1Id);
    }
}
