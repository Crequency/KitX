// ─────────────────────────────────────────────────────────────────────────────
// Multi-line source-list tests (2026-08-02): comma line-breaks with strict indent,
// inline comments attaching to sources, comment-driven render folding, and the
// BP→KS source-comment round-trip.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.BpGraphLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class MultiLineSourceTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public MultiLineSourceTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact]
    public void Comma_LineBreak_Parses_Multiple_Sources()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int b
                bool cond
            }
            a,
                b > Compare("BEQ") > cond
            """, []);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Equal(2, pipe.Sources.Length);
        Assert.Equal("Compare", pipe.Segments[0].Target);
    }

    [Fact]
    public void Comma_Inline_Comment_Attaches_To_Source()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int b
                bool cond
            }
            a, // Comment4a
                b > Compare("BEQ") > cond
            """, []);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Equal("Comment4a", pipe.Sources[0].Comment);
        Assert.Equal(2, pipe.Sources.Length);
    }

    [Fact]
    public void Strict_Indent_Violation_Reports_KS066()
    {
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics("""
            var {
                int a
                int b
            }
            a,
            b > Print
            """);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS066");
    }

    [Fact]
    public void FullLine_Comment_Between_Source_Continuations_Reports_KS065()
    {
        var (_, diag) = _fixture.KsLens.ParseAstWithDiagnostics("""
            var {
                int a
                int b
            }
            a,
                // 整行注释
                b > Print
            """);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS065");
    }

    [Fact]
    public void Render_Folds_Commented_Sources_Line_By_Line()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int b
                bool cond
            }
            a, // 注释a
                b > Compare("BEQ") > cond
            """, []);
        var rendered = _fixture.KsLens.Project(ir);
        Assert.True(rendered.Contains("a, // 注释a"), $"Rendered:\n{rendered}");
        // Re-parse keeps the source comment (round-trip).
        var re = _fixture.KsLens.Parse(rendered, []);
        var pipe = Assert.IsType<PipelineStatement>(re.Body[0]);
        Assert.Equal("注释a", pipe.Sources[0].Comment);
    }

    [Fact]
    public void Render_Folds_Segments_By_Comment()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
            }
            a
                > Add(_, 1) > Print  // 段注释
                > Pause(1)
            """, []);
        var rendered = _fixture.KsLens.Project(ir);
        // Comment-free Add and commented Print share a line; Pause starts a new line.
        Assert.True(rendered.Contains("> Add(_, 1) > Print // 段注释"), $"Rendered:\n{rendered}");
        var pauseLine = rendered.Split('\n').First(l => l.Contains("> Pause"));
        Assert.Contains("    > Pause", pauseLine);
    }

    [Fact]
    public void Reverse_Restores_Source_Comment_From_BP()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int b
                bool cond
            }
            a, // 源注释
                b > Compare("BEQ") > cond
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var text = _fixture.KsLens.Project(reversed);
        Assert.Contains("源注释", text);
    }

    [Fact]
    public void Condition_Header_Comma_LineBreak_And_Comment()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int b
            }
            if a, // 条件源注释
                b > Compare("BEQ"):
                Print("yes")
            """, []);
        var iff = Assert.IsType<IfStatement>(ir.Body[0]);
        var cond = Assert.IsType<KsPipeline>(iff.Condition);
        Assert.Equal(2, cond.Sources.Length);
        Assert.Equal("条件源注释", cond.Sources[0].Comment);
    }
}

