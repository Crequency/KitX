// ─────────────────────────────────────────────────────────────────────────────
// ReverseNodePathMappingTests — BpGraphLens.ReverseWithNodePaths' canvas-id →
// canonical-id map must align with the ids a re-projection produces.
//
// The canonical id is NodeId.Of(path) where path is the BpRenderer path of the
// node. If the reverse translator's path assignment ever drifts from BpRenderer's,
// the canonical ids stop matching Project(ir)'s node ids — which would silently
// break breakpoint migration and layout persistence (T5). These tests pin the
// symmetry across all statement shapes.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Diff;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.BpGraphLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class ReverseNodePathMappingTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public ReverseNodePathMappingTests(WorkflowTestFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Round-trips <paramref name="ksSrc"/> and asserts the mapping contract:
    /// 1. Every map key is a canvas node id (subset of the source blueprint).
    /// 2. Every map value is a real node id of the re-projected blueprint.
    /// 3. Every non-root node of the source blueprint is covered by the map.
    /// (Entry/PluginTriggerNode are excluded; DetachedGraph nodes are covered by
    /// the dedicated detached test.)
    /// </summary>
    private void AssertMapAligns(string ksSrc)
    {
        var ir = _fixture.KsLens.Parse(ksSrc, []);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);

        var (reversed, map) = lens.ReverseWithNodePaths(bp);

        // Sanity: reverse must still be structurally equivalent (existing guarantee).
        var diff = WorkflowDiffer.Compute(ir, reversed);
        Assert.True(diff.IsEmpty,
            $"Round-trip diff should be empty: {diff.StatementChanges.Length} changes: "
            + string.Join(" | ", diff.StatementChanges.Select(c => $"{c.Kind}@{c.LexicalPath}")));

        var canvasIds = bp.Nodes.Select(n => n.Id).ToHashSet();
        var projectedIds = lens.Project(reversed).Nodes.Select(n => n.Id).ToHashSet();

        Assert.All(map, kv =>
        {
            Assert.Contains(kv.Key, canvasIds);
            Assert.True(projectedIds.Contains(kv.Value),
                $"canonical id {kv.Value} (from canvas node {kv.Key}) is not a re-projected node id");
        });

        // Coverage: every non-root node must be mapped (nothing silently unmapped —
        // an unmapped node means breakpoints/layout can't migrate for it).
        foreach (var node in bp.Nodes)
        {
            if (node is EntryNode or PluginTriggerNode) continue;
            Assert.True(map.ContainsKey(node.Id),
                $"node {node.Id} ({node.GetType().Name} '{node.Name}') missing from the id map");
        }
    }

    [Fact]
    public void Bare_Call_Node_Maps_To_Its_Stmt_Path()
    {
        var ir = _fixture.KsLens.Parse("Print(\"hello\")\n", []);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var (reversed, map) = lens.ReverseWithNodePaths(bp);
        var printNode = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "Print");
        var projectedId = lens.Project(reversed).Nodes.Single(n => n.Name == "Print").Id;
        Assert.Equal(projectedId, map[printNode.Id]);
    }

    [Fact]
    public void Map_Aligns_Simple_Pipeline()
    {
        AssertMapAligns("""
            var {
                int counter
            }
            0 > counter
            counter, 1 > Add > counter
            counter > Print
            """);
    }

    [Fact]
    public void Map_Aligns_If_Else_With_Branch_Bodies()
    {
        AssertMapAligns("""
            var {
                bool cond
            }
            if cond:
                Print("yes")
            else:
                Print("no")
            Print("after")
            """);
    }

    [Fact]
    public void Map_Aligns_Pipeline_Condition()
    {
        // Condition sub-graph `a, b > Compare("BEQ")` (3 nodes under /cond) must map.
        AssertMapAligns("""
            var {
                int a
                int b
            }
            if a, b > Compare("BEQ", _, _):
                Print("equal")
            """);
    }

    [Fact]
    public void Map_Aligns_ForEach_With_Function_Source()
    {
        AssertMapAligns("""
            forEach Range(0, 3, 1) as i:
                i > Print
            """);
    }

    [Fact]
    public void Map_Aligns_ForEach_With_Pipeline_Source()
    {
        AssertMapAligns("""
            var {
                int loopMax
            }
            forEach loopMax > Range(0, _, 1) as i:
                i > Print
            """);
    }

    [Fact]
    public void Map_Aligns_While_With_Pipeline_Condition()
    {
        AssertMapAligns("""
            var {
                int counter
            }
            while counter, 3 > Compare("BLT", _, _):
                counter, 1 > Add > counter
            """);
    }

    [Fact]
    public void Map_Aligns_Switch_With_Arms_And_Default()
    {
        AssertMapAligns("""
            var {
                int sel
            }
            switch sel:
                1:
                    Print("one")
                2:
                    Print("two")
                default:
                    Print("other")
            Print("after")
            """);
    }

    [Fact]
    public void Map_Aligns_Break_And_Continue()
    {
        AssertMapAligns("""
            var {
                int counter
            }
            while counter, 3 > Compare("BLT", _, _):
                counter, 1 > Add > counter
                if counter, 2 > Compare("BEQ", _, _):
                    break
                continue
            """);
    }

    [Fact]
    public void Map_Aligns_Deeply_Nested_Scopes()
    {
        AssertMapAligns("""
            const {
                int loopMax = 3
            }
            var {
                int counter
            }
            forEach loopMax > Range(0, _, 1) as i:
                while counter, 5 > Compare("BLT", _, _):
                    if counter, 2 > Compare("BEQ", _, _):
                        counter, 1 > Add > counter
                    else:
                        Print("tick")
                i > Print
            """);
    }

    [Fact]
    public void Map_Aligns_Definition_Nodes()
    {
        var ir = _fixture.KsLens.Parse("""
            const {
                int max = 10
                string name = "hello"
            }
            var {
                bool flag
            }
            max > Print
            """, []);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);
        var (reversed, map) = lens.ReverseWithNodePaths(bp);
        var projected = lens.Project(reversed);
        foreach (var defNode in bp.Nodes.Where(n => n is ConstNode { IsDefinition: true } or VariableNode { IsDefinition: true }))
        {
            Assert.True(map.ContainsKey(defNode.Id), $"definition node {defNode.Id} not mapped");
            Assert.Contains(map[defNode.Id], projected.Nodes.Select(n => n.Id));
        }
    }

    [Fact]
    public void Map_Aligns_Function_Source_And_Var_Tap_Chain()
    {
        // `PluginCall("A", "B")` as a group-leading function source + var-tap chain.
        AssertMapAligns("""
            var {
                dict d
            }
            PluginCall("A", "B") > JsonToDict > d
            d > Print
            """);
    }

    [Fact]
    public void Map_Excludes_Entry_And_Detached_Graph_Nodes()
    {
        var ir = _fixture.KsLens.Parse("Print(\"hello\")\n", []);
        var lens = _fixture.BpLens;
        var bp = lens.Project(ir);

        // Fabricate a detached component: a node cluster not reachable from Entry.
        var detachedFn = new BuiltinFunctionNode { Name = "Print", FunctionName = "Print" };
        detachedFn.InputPins.Add(new BlueprintPin { Name = "Exec", Direction = PinDirection.Input, Type = PinType.Execution });
        detachedFn.OutputPins.Add(new BlueprintPin { Name = "Exec", Direction = PinDirection.Output, Type = PinType.Execution });
        detachedFn.OutputPins.Add(new BlueprintPin { Name = "Value", Direction = PinDirection.Output, Type = PinType.Any });
        detachedFn.Id = "n_DETACHED1234";
        bp.Nodes.Add(detachedFn);

        var (reversed, map) = lens.ReverseWithNodePaths(bp);

        Assert.Contains(detachedFn.Id, reversed.DetachedGraphs.SelectMany(g => g.Nodes.Select(n => n.Id)));
        Assert.False(map.ContainsKey(detachedFn.Id), "detached graph nodes must not be in the id map");
        Assert.DoesNotContain(bp.Nodes.OfType<EntryNode>(), e => map.ContainsKey(e.Id));
    }
}
