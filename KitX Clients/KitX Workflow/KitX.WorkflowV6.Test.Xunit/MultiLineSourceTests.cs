// ─────────────────────────────────────────────────────────────────────────────
// Multi-line source-list tests (2026-08-02): comma line-breaks with strict indent,
// inline comments attaching to sources/segments, comment-driven render folding,
// and precise BP→KS comment round-trips (each comment points at its nearest node).
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
        // Multi-line condition header: continuation lines and the body's first line sit
        // at the same indent (header+1); the `>` prefix / colon terminator disambiguates
        // continuation vs body per the grammar.
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

    // ── Precise comment round-trips (each comment points at its NEAREST node) ──

    [Fact]
    public void RoundTrip_Last_Segment_Comment_Stays_On_Last_Segment()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
            }
            a
                > Add(_, 1)
                > Print  // 末段注释
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var pipe = Assert.IsType<PipelineStatement>(reversed.Body[0]);
        // The last segment keeps its comment; the statement has NO trailing comment.
        Assert.Equal("末段注释", pipe.Segments[^1].Comment);
        Assert.Null(pipe.TrailingComment);
        var text = _fixture.KsLens.Project(reversed);
                Assert.Contains("> Print // 末段注释", text);
    }

    [Fact]
    public void RoundTrip_SingleLine_Trailing_Comment_Stays_On_Last_Segment()
    {
        // `a > Print // cmt` — the inline comment belongs to the NEAREST node (Print),
        // not the statement: Parse→Project→Reverse keeps it on the last segment, and
        // rendering keeps the single-line form.
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
            }
            a > Print  // 语句尾注释
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var pipe = Assert.IsType<PipelineStatement>(reversed.Body[0]);
        Assert.Equal("语句尾注释", pipe.Segments[^1].Comment);
        Assert.Null(pipe.TrailingComment);
        var text = _fixture.KsLens.Project(reversed);
                Assert.True(text.Contains("a > Print // 语句尾注释"), $"Text:\n{text}");
    }

    [Fact]
    public void RoundTrip_Tap_Segment_Comment_Stays_On_Tap_Segment()
    {
        var ir = _fixture.KsLens.Parse("""
            var {
                int a
                int counter
            }
            a
                > counter  // tap 注释
            """, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var pipe = Assert.IsType<PipelineStatement>(reversed.Body[0]);
        Assert.Single(pipe.Segments);
        Assert.True(pipe.Segments[0].IsVariableTap);
        Assert.Equal("tap 注释", pipe.Segments[0].Comment);
        Assert.Null(pipe.TrailingComment);
    }

    [Fact]
    public void RoundTrip_User_Scenario_Comments_On_Nearest_Nodes()
    {
        // User scenario: `targetNum > Compare("BEQ", _, _) // Comment4Compare` — the
        // comment must land on the Compare SEGMENT (nearest node), never on the targetNum
        // source, and survive a full round-trip without shifting.
        var ir = _fixture.KsLens.Parse("""
            const {
                int targetNum
                int loopMax
                int guessNum
            }
            var {
                bool cond
                int i
            }
            forEach loopMax > Range(0, _, 1) as i:
                guessNum, // Comment4guessNum
                    targetNum > Compare("BEQ", _, _) // Comment4Compare
                    > cond // Comment4cond
                if cond:
                    Print("correct!") // 猜对了
            """, []);
        var fe = Assert.IsType<ForEachStatement>(ir.Body[0]);
        var pipe = Assert.IsType<PipelineStatement>(fe.Body[0]);
        Assert.Equal("Comment4guessNum", pipe.Sources[0].Comment);
        Assert.Equal("Comment4Compare", pipe.Segments[0].Comment);
        Assert.Equal("Comment4cond", pipe.Segments[1].Comment);

        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var fe2 = Assert.IsType<ForEachStatement>(reversed.Body[0]);
        var pipe2 = Assert.IsType<PipelineStatement>(fe2.Body[0]);
        Assert.Equal("Comment4guessNum", pipe2.Sources[0].Comment);
        Assert.Equal("Comment4Compare", pipe2.Segments[0].Comment);
        Assert.Equal("Comment4cond", pipe2.Segments[1].Comment);
        var text = _fixture.KsLens.Project(reversed);
                Assert.True(text.Contains("guessNum, // Comment4guessNum"), $"Text:\n{text}");
        Assert.True(text.Contains("// Comment4Compare"), $"Text:\n{text}");
        Assert.True(text.Contains("// Comment4cond"), $"Text:\n{text}");
    }

    [Fact]
    public void RoundTrip_Full_User_Program_No_Comment_Shift()
    {
        const string src = """
            const {
                int targetNum
                int loopMax
                int guessNum
            }
            var {
                bool cond
                int i
            }
            // 开始游戏
            Print("start")
            // 循环
            forEach loopMax > Range(0, _, 1) as i:
                // 测试组注释无交互问题
                guessNum, // Comment4guessNum
                    targetNum > Compare("BEQ", _, _) // Comment4Compare
                    > cond // Comment4cond
                if cond:
                    Print("correct!") // 猜对了
                    break
                else:
                    if guessNum, targetNum > Compare("BLT", _, _):
                        Print("too small") // 猜小了吗
                    else:
                        Print("too big") // 猜大了
            // 结束消息
            Print("end") // 游戏结束
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        var text = _fixture.KsLens.Project(reversed);
        
        // Every comment stays on its nearest node / original line — no shifts.
        Assert.True(text.Contains("guessNum, // Comment4guessNum"), $"Text:\n{text}");
        Assert.True(text.Contains("targetNum > Compare(\"BEQ\", _, _) // Comment4Compare")
                 || text.Contains("> Compare(\"BEQ\", _, _) // Comment4Compare"), $"Text:\n{text}");
        Assert.True(text.Contains("> cond // Comment4cond"), $"Text:\n{text}");
        Assert.True(text.Contains("Print(\"correct!\") // 猜对了"), $"Text:\n{text}");
        Assert.True(text.Contains("Print(\"too small\") // 猜小了吗"), $"Text:\n{text}");
        Assert.True(text.Contains("Print(\"too big\") // 猜大了"), $"Text:\n{text}");
        Assert.True(text.Contains("Print(\"end\") // 游戏结束"), $"Text:\n{text}");
    }
}








