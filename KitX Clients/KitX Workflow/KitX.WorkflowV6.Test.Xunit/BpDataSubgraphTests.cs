// ─────────────────────────────────────────────────────────────────────────────
// Data-subgraph tests (2026-08-02 definition alignment).
//
// A data subgraph is the connected component of DATA edges reachable from a
// statement's primary node. Theorems under test:
//   1. KS one line (statement) ⇔ exactly one data subgraph; subgraphs never overlap.
//   2. Every node with a data connection belongs to exactly one subgraph
//      (Blueprint.StatementNodeToPrimary covers them all).
//   3. A statement with no data edges still keeps its primary node (NodeIds=[primary]).
//   4. Multi-line pipelines reject full-line comments between continuations (KS065).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Lens.BpGraphLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class BpDataSubgraphTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public BpDataSubgraphTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact]
    public void Data_Subgraphs_Are_Mutually_Exclusive_And_Cover_Data_Nodes()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int b
                int c
                bool cond
            }
            // 注释A
            a, b > Compare("BEQ") > cond
            // 注释B
            c > Print
            """, []);
        var bp = _fixture.BpLens.Project(ir);

        var gcA = bp.GroupComments.Single(gc => gc.Comment.Contains("注释A"));
        var gcB = bp.GroupComments.Single(gc => gc.Comment.Contains("注释B"));

        // 1: subgraphs do not overlap.
        Assert.Empty(gcA.NodeIds.Intersect(gcB.NodeIds));

        // A contains the Compare function node; B contains the Print node.
        Assert.Contains(bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Compare").Id, gcA.NodeIds);
        Assert.Contains(bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Print").Id, gcB.NodeIds);
    }

    [Fact]
    public void StatementNodeToPrimary_Maps_Every_Subgraph_Node_To_Its_Primary()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int b
                int c
                bool cond
            }
            // 注释A
            a, b > Compare("BEQ") > cond
            // 注释B
            c > Print
            """, []);
        var bp = _fixture.BpLens.Project(ir);

        foreach (var gc in bp.GroupComments)
            foreach (var id in gc.NodeIds)
                Assert.Equal(gc.AnchorNodeId, bp.StatementNodeToPrimary[id]);

        // Every node touched by a data edge is covered by the mapping.
        foreach (var conn in bp.Connections)
        {
            var src = bp.Nodes.FirstOrDefault(n => n.Id == conn.SourceNodeId);
            var srcPin = src?.OutputPins.Find(p => p.Id == conn.SourcePinId);
            if (srcPin is null || srcPin.Type == PinType.Execution) continue;
            Assert.True(bp.StatementNodeToPrimary.ContainsKey(conn.SourceNodeId), $"source {conn.SourceNodeId} unmapped");
            Assert.True(bp.StatementNodeToPrimary.ContainsKey(conn.TargetNodeId), $"target {conn.TargetNodeId} unmapped");
        }
    }

    [Fact]
    public void Nested_If_Body_Node_Does_Not_Belong_To_If_Data_Subgraph()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                bool cond
            }
            // if 注释
            if cond:
                Print("in")
            """, []);
        var bp = _fixture.BpLens.Project(ir);

        var gcIf = bp.GroupComments.Single();
        var branch = bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Branch");
        var print = bp.Nodes.OfType<BuiltinFunctionNode>().First(n => n.FunctionName == "Print");

        // The if statement's data subgraph = condition subgraph (cond usage + Branch via
        // its Condition data pin); the body Print belongs to its OWN statement, not the if.
        Assert.Contains(branch.Id, gcIf.NodeIds);
        Assert.Contains(condUsageNode(bp, "cond"), gcIf.NodeIds);
        Assert.DoesNotContain(print.Id, gcIf.NodeIds);
        Assert.Equal(print.Id, bp.StatementNodeToPrimary[print.Id]);
    }

    [Fact]
    public void Statement_Without_Data_Edges_Keeps_Its_Primary_Node()
    {
        var ir = _fixture.KsLens.Parse("""
            // 注释
            Print("x")
            """, []);
        var bp = _fixture.BpLens.Project(ir);

        var gc = bp.GroupComments.Single();
        Assert.Single(gc.NodeIds);
        Assert.Equal(gc.AnchorNodeId, gc.NodeIds[0]);
    }

    [Fact]
    public void MultiLine_Pipeline_Rejects_FullLine_Comment_Between_Continuations()
    {
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics("""
            var {
                int a
                int b
            }
            a, b
                > Compare("BEQ")  // 行内注释 OK
                // 整行注释 应拒绝
                > Print
            """);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS065");
    }

    [Fact]
    public void MultiLine_Pipeline_Condition_Rejects_FullLine_Comment_Between_Continuations()
    {
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics("""
            var {
                int a
                int b
            }
            if a, b
                > Compare("BEQ")
                // 整行注释 应拒绝
                > Print("yes"):
                Print("ok")
            """);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS065");
    }

    [Fact]
    public void MultiLine_Pipeline_Inline_Comments_Are_Still_Allowed()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int b
                bool cond
            }
            a, b
                > Compare("BEQ")  // 比较
                > cond
            """, []);
        // No KS065; parses clean with per-segment comments preserved.
        Assert.Single(ir.Body);
    }

    private static string condUsageNode(Blueprint bp, string varName)
        => bp.Nodes.OfType<VariableNode>().First(n => n.VarName == varName && !IsDefinitionNode(n)).Id;

    private static bool IsDefinitionNode(BlueprintNode n)
        => !n.InputPins.Any(p => p.Type == PinType.Execution)
           && !n.OutputPins.Any(p => p.Type == PinType.Execution);
}
