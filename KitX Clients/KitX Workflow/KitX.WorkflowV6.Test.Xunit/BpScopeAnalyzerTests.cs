// ─────────────────────────────────────────────────────────────────────────────
// ScopeAnalyzer region tests — verifies sub-scope NodeIds collection, especially
// for nested control flow (regression: regions[^1] was clobbered by nested adds,
// leaving outer sub-scope frames empty).
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Lens.BpGraphLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class BpScopeAnalyzerTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public BpScopeAnalyzerTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact]
    public void AnalyzeScopes_Nested_If_Populates_Outer_And_Inner_NodeIds()
    {
        var ir = _fixture.KsLens.Parse("""
            if true:
                Print("a")
            else:
                if true:
                    Print("b")
                else:
                    Print("c")
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var scopes = _fixture.BpLens.AnalyzeScopes(bp);

        var elseRegions = scopes.Where(s => s.ScopeKind == "Else").ToList();
        Assert.Equal(2, elseRegions.Count);   // outer else (nested if) + inner else

        // Outer else must be populated (regression: regions[^1] clobbering).
        var outerElse = elseRegions.OrderBy(s => s.Depth).First();
        Assert.NotEmpty(outerElse.NodeIds);
        Assert.True(outerElse.Width > 0 && outerElse.Height > 0);

        // Inner scopes must be populated too.
        var thenRegions = scopes.Where(s => s.ScopeKind == "Then").ToList();
        Assert.Equal(2, thenRegions.Count);
        Assert.All(thenRegions, t => Assert.NotEmpty(t.NodeIds));
    }

    [Fact]
    public void AnalyzeScopes_ForEach_Body_Is_Populated()
    {
        var ir = _fixture.KsLens.Parse("""
            forEach Range(0, 3, 1) as i:
                i > Print
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var scopes = _fixture.BpLens.AnalyzeScopes(bp);

        var body = scopes.Single(s => s.ScopeKind == "Body");
        Assert.NotEmpty(body.NodeIds);
        Assert.True(body.Width > 0 && body.Height > 0);
    }

    [Fact]
    public void Project_Collects_Statement_Primary_Nodes()
    {
        // Statements: if (Branch) + body Print(a) + else-body Print(b) + trailing Print.
        var ir = _fixture.KsLens.Parse("""
            if true:
                Print("a")
            else:
                Print("b")
            Print("after")
            """, []);
        var bp = _fixture.BpLens.Project(ir);

        Assert.Equal(4, bp.StatementPrimaryNodeIds.Count);
        Assert.All(bp.StatementPrimaryNodeIds, id => Assert.Contains(bp.Nodes, n => n.Id == id));

        var branch = bp.Nodes.OfType<KitX.Core.Contract.Workflow.BuiltinFunctionNode>()
            .First(n => n.FunctionName == "Branch");
        Assert.Contains(branch.Id, bp.StatementPrimaryNodeIds);
    }
}
