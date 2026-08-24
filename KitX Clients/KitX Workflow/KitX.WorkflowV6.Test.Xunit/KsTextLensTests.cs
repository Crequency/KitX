// ─────────────────────────────────────────────────────────────────────────────
// Phase 2 acceptance tests for KsTextLens (KS indented parser + lowerer + renderer).
//
// Covers the KS text round-trip pipeline:
//   • Parse empty program
//   • Parse const/var blocks
//   • Parse if/else, forEach, while, switch, nested control flow
//   • Parse break/continue
//   • Parse pipelines (bare call, multi-segment, with assignment tap)
//   • Reject Tab characters in indentation (§十二-A)
//   • Report indent errors with line/column
//   • Round-trip idempotence: parse → render → parse ≡ id (modulo formatting)
//   • Comments are preserved through round-trip
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.KsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Unit")]
public class KsTextLensTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public KsTextLensTests(WorkflowTestFixture fixture) => _fixture = fixture;

    // ── Parse empty program ──

    [Fact]
    public void Parse_Empty_Program()
    {
        var ir = _fixture.KsLens.Parse("", []);
        Assert.Empty(ir.Body);
        Assert.Empty(ir.Constants);
        Assert.Empty(ir.GlobalVars);
    }

    // ── Parse const/var blocks ──

    [Fact]
    public void Parse_Const_Var_Blocks()
    {
        var src = """
            const {
                int loopMax = 5
                string greeting = "hello"
            }

            var {
                int counter
                string message
            }

            Print("start")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Equal(2, ir.Constants.Count);
        Assert.Equal("int", ir.Constants["loopMax"].Type);
        Assert.Equal("5", ir.Constants["loopMax"].InitialValueExpression);
        Assert.Equal(2, ir.GlobalVars.Count);
        Assert.Equal("int", ir.GlobalVars["counter"].Type);
        Assert.Null(ir.GlobalVars["counter"].InitialValueExpression);
        Assert.Single(ir.Body);  // the Print statement
    }

    // ── Parse if/else ──

    [Fact]
    public void Parse_If_Else()
    {
        var src = """
            if cond:
                Print("then")
            else:
                Print("else")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Single(ir.Body);
        var iff = Assert.IsType<IfStatement>(ir.Body[0]);
        Assert.NotNull(iff.Condition);
        Assert.Single(iff.ThenBody);
        Assert.Single(iff.ElseBody);
        Assert.IsType<PipelineStatement>(iff.ThenBody[0]);
        Assert.IsType<PipelineStatement>(iff.ElseBody[0]);
    }

    // ── Parse forEach ──

    [Fact]
    public void Parse_ForEach()
    {
        var src = """
            forEach Range(0, 10, 1) as i:
                i > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Single(ir.Body);
        var fe = Assert.IsType<ForEachStatement>(ir.Body[0]);
        Assert.Equal("i", fe.ItemName);
        Assert.Single(fe.Body);
        Assert.IsType<PipelineStatement>(fe.Body[0]);
    }

    [Fact]
    public void Parse_ForEach_Pipeline_Source()
    {
        // forEach accepts pipeline expressions as source (like if/while conditions):
        // `forEach loopMax > Range(0, _, 1) as i` — loopMax flows into Range via pipeline.
        var src = """
            const {
                int loopMax = 10
            }

            forEach loopMax > Range(0, _, 1) as i:
                i > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var fe = ir.Body.OfType<ForEachStatement>().Single();
        Assert.Equal("i", fe.ItemName);
        // Source should be a KsPipeline (loopMax > Range(0, _, 1)).
        Assert.IsType<KsPipeline>(fe.Source);
        Assert.Single(fe.Body);
    }

    [Fact]
    public void RoundTrip_ForEach_Pipeline_Source()
    {
        // Verify the pipeline-source forEach form round-trips: KS → IR → KS → re-parseable.
        var src = """
            const {
                int loopMax = 3
            }

            forEach loopMax > Range(0, _, 1) as i:
                i > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir);
        // The rendered output must be re-parseable (no round-trip breakage).
        var reIr = _fixture.KsLens.Parse(rendered, []);
        // Compare the forEach source type and item name explicitly.
        var origFe = ir.Body.OfType<ForEachStatement>().Single();
        var reFe = reIr.Body.OfType<ForEachStatement>().Single();
        Assert.Equal(origFe.ItemName, reFe.ItemName);
        Assert.Equal(origFe.Source.GetType(), reFe.Source.GetType());
        // Compare fingerprints (structural equality, excludes SourceText/SourceLine).
        Assert.Equal(origFe.Fingerprint, reFe.Fingerprint);
    }

    // ── Parse while ──

    [Fact]
    public void Parse_While()
    {
        var src = """
            while cond:
                Print("body")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Single(ir.Body);
        var ws = Assert.IsType<WhileStatement>(ir.Body[0]);
        Assert.Single(ws.Body);
    }

    // ── Parse nested if ──

    [Fact]
    public void Parse_Nested_If()
    {
        var src = """
            if outer:
                Print("outer then")
                if inner:
                    Print("inner then")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Single(ir.Body);
        var outer = Assert.IsType<IfStatement>(ir.Body[0]);
        Assert.Equal(2, outer.ThenBody.Length);
        Assert.IsType<IfStatement>(outer.ThenBody[1]);
    }

    // ── Parse break/continue ──

    [Fact]
    public void Parse_Loop_Control_Statements()
    {
        var src = """
            forEach Range(0, 5, 1) as i:
                break
                continue
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var fe = Assert.IsType<ForEachStatement>(ir.Body[0]);
        Assert.Equal(2, fe.Body.Length);
        Assert.IsType<BreakStatement>(fe.Body[0]);
        Assert.IsType<ContinueStatement>(fe.Body[1]);
    }

    // ── Tab rejected ──

    [Fact]
    public void Parse_Tab_Rejected()
    {
        var src = "if cond\n\tPrint(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS001");
    }

    // ── Indent error ──

    [Fact]
    public void Parse_Indent_Error_Mismatch()
    {
        // 3-space indent is not a multiple of 4 — must report KS002.
        var src = "if cond\n   Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS002");
    }

    // ── Round-trip idempotence ──

    [Fact]
    public void RoundTrip_Idempotent_Simple_Pipeline()
    {
        var src = "Print(\"hello\")\n";
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Idempotent_If_Else()
    {
        var src = """
            if cond:
                Print("then")
            else:
                Print("else")
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Idempotent_ForEach()
    {
        var src = """
            forEach Range(0, 10, 1) as i:
                i > Print
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Idempotent_Nested_If()
    {
        var src = """
            if outer:
                Print("outer then")
                if inner:
                    Print("inner then")
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Idempotent_While_With_Break()
    {
        var src = """
            while cond:
                Print("body")
                break
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    // ── Comments preserved (v5.1 §9 three-form anchoring: leading / trailing / segment) ──

    [Fact]
    public void Parse_Inline_Comment_Preserved()
    {
        // An inline `//` comment attaches as the statement's TrailingComment.
        var src = "Print(\"x\") // this is a comment\n";
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Single(ir.Body);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Equal("this is a comment", pipe.TrailingComment);
        Assert.Null(pipe.LeadingComment);
    }

    [Fact]
    public void Parse_Full_Line_Comment_Preserved()
    {
        // A full-line `//` comment attaches as the next statement's LeadingComment.
        var src = """
            // this is a full-line comment
            Print("x")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Single(ir.Body);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Equal("this is a full-line comment", pipe.LeadingComment);
        Assert.Null(pipe.TrailingComment);
    }

    [Fact]
    public void Parse_Consecutive_Leading_Comments_Joined()
    {
        // Multiple consecutive full-line comments join into one LeadingComment (\n-separated).
        var src = """
            // first line
            // second line
            Print("x")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Equal("first line\nsecond line", pipe.LeadingComment);
    }

    [Fact]
    public void Comment_Leading_Trip()
    {
        var src = """
            // leading comment
            a, b > Compare("BEQ") > Print
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        Assert.Contains("// leading comment", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void Comment_Trailing_Trip()
    {
        var src = "a, b > Compare(\"BEQ\") > Print // trailing comment\n";
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        Assert.Contains("// trailing comment", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void Comment_Multiline_Segment_Trip()
    {
        // A segment-level comment forces the multi-line rendering and round-trips.
        var src = """
            // leading
            a, b // source inline
                > Compare("BEQ") // compare comment
                > Print // print comment
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var pipe = Assert.IsType<PipelineStatement>(ir1.Body[0]);
        Assert.Equal("leading", pipe.LeadingComment);
        Assert.Equal("source inline", pipe.TrailingComment);
        Assert.Equal("compare comment", pipe.Segments[0].Comment);
        Assert.Equal("print comment", pipe.Segments[1].Comment);

        var rendered = _fixture.KsLens.Project(ir1);
        // Multi-line form: each segment on its own line.
        Assert.Contains("> Compare(\"BEQ\") // compare comment", rendered);
        Assert.Contains("> Print // print comment", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void Comment_ControlFlow_Trailing_Trip()
    {
        // A trailing comment on a control-flow header line round-trips.
        var src = """
            // loop guard
            while cond: // keep looping
                Print("tick")
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        Assert.Contains("// keep looping", rendered);
        Assert.Contains("// loop guard", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void Standard_Format_RoundTrip_Stable_With_Comments()
    {
        // A multi-statement program with all comment kinds (leading/trailing/segment/
        // multi-line leading merge) round-trips with IR equality — equality is now
        // purely semantic (SourceLine/SourceText excluded), so line/format drift from
        // rendering does not break the round-trip.
        var src = """
            // top leading
            // second leading line
            Print("start") // trailing

            // loop doc
            forEach Range(0, 3, 1) as i: // iter
                // body leading
                i > Print // body trailing
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    // ── Project renders correct indentation ──

    [Fact]
    public void Project_Renders_Correct_Indentation()
    {
        var src = """
            if cond:
                Print("then")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir);
        // The then body must be indented by 4 spaces.
        Assert.Contains("\n    Print(\"then\")", rendered);
    }

    [Fact]
    public void Project_Renders_Nested_Indentation()
    {
        var src = """
            if outer:
                if inner:
                    Print("deep")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir);
        // The innermost Print must be indented by 8 spaces.
        Assert.Contains("\n        Print(\"deep\")", rendered);
    }

    // ── ParseAst produces KsProgram ──

    [Fact]
    public void ParseAst_Returns_KsProgram()
    {
        var src = "Print(\"x\")\n";
        var ast = _fixture.KsLens.ParseAst(src);
        var program = Assert.IsType<KsProgram>(ast);
        Assert.Single(program.Body);
        Assert.IsType<KsPipeline>(program.Body[0]);
    }

    // ── Error scenario coverage (KS0xx codes) ──

    [Fact]
    public void Error_KS051_Identifier_In_Function_Parens()
    {
        // v6.0 rule: function parens may only contain literals/placeholders.
        // `Print(myVar)` — myVar is an identifier inside parens → KS051.
        var src = "Print(myVar)\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS051");
    }

    [Fact]
    public void Error_KS051_Identifier_In_Segment_Parens()
    {
        // `1 > Add(x, _)` — x is an identifier inside segment parens → KS051.
        var src = "1 > Add(x, _)\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS051");
    }

    [Fact]
    public void Error_KS051_Not_Raised_For_Literal_Args()
    {
        // `Print("hello")` — all-literal args → no KS051.
        var src = "Print(\"hello\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.False(diag.HasErrors);
    }

    [Fact]
    public void Error_KS051_Not_Raised_For_Placeholder()
    {
        // `1 > Range(0, _, 1)` — _ is a placeholder, not an identifier → no KS051.
        var src = "1 > Range(0, _, 1)\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.False(diag.HasErrors);
    }

    [Fact]
    public void Error_KS030_Missing_As_After_ForEach()
    {
        var src = "forEach Range(0, 3, 1)\n    Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS030");
    }

    [Fact]
    public void Error_KS042_Unterminated_Call_Args()
    {
        var src = "Print(\"hello\"\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS042" || d.Code == "KS052");
    }

    [Fact]
    public void Error_KS062_Empty_If_Body()
    {
        var src = "if cond\nPrint(\"not indented\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS062");
    }

    [Fact]
    public void Error_KS010_Top_Level_Not_Indent_Zero()
    {
        // Statement at indent 2 (not 0) at top level.
        var src = "    Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS010");
    }

    [Fact]
    public void Error_KS011_Duplicate_Const_Block()
    {
        var src = """
            const {
                int a = 1
            }

            const {
                int b = 2
            }
            """;
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS011");
    }

    [Fact]
    public void Error_Collection_Contains_All_Error_Codes()
    {
        // Multiple errors in one source — all should be collected (error recovery).
        var src = "Print(myVar)\nPrint(otherVar)\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        // Both lines should produce KS051.
        var ks051Count = diag.Items.Count(d => d.Code == "KS051");
        Assert.True(ks051Count >= 2, $"Expected >=2 KS051 errors, got {ks051Count}");
    }

    // ── Systematic KS0xx error code coverage (remaining 11 codes) ──

    [Fact]
    public void Error_KS012_Declaration_Missing_Type_Name()
    {
        // const row missing type identifier: "5" is IntegerLiteral, not Identifier.
        var src = "const {\n    5\n}\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS012");
    }

    [Fact]
    public void Error_KS013_Missing_LBrace_After_Const()
    {
        // "const int x = 5" — const followed by identifier, not "{".
        var src = "const int x = 5\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS013");
    }

    [Fact]
    public void Error_KS020_Switch_Case_Missing_Colon()
    {
        // Case label without ":" separator.
        var src = "switch sel\n    0 Print(\"zero\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS020");
    }

    [Fact]
    public void Error_KS021_Switch_Arm_Invalid_Label()
    {
        // Arm label must be integer or "default"; "x" is an identifier.
        var src = "switch sel\n    x:\n        Print(\"zero\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS021");
    }

    [Fact]
    public void Error_KS022_Duplicate_Default_Arm()
    {
        var src = "switch sel\n    default:\n        Print(\"a\")\n    default:\n        Print(\"b\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS022");
    }

    [Fact]
    public void Parse_Switch_FullLine_Comment_Between_Arms_No_KS021()
    {
        // B5b: a full-line comment between switch arms used to be misread as an arm
        // label ("Expected case label or 'default'", KS021). It now accumulates as
        // the leading comment of the NEXT arm's first statement (same semantics as
        // body comments in ParseBody) and must not produce any error.
        var src = """
            switch sel:
                0:
                    Print("zero")
                // between arms
                1:
                    Print("one")
                // before default
                default:
                    Print("other")
            """;
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.False(diag.HasErrors, string.Join("; ", diag.Items.Select(d => d.Code)));
        Assert.DoesNotContain(diag.Items, d => d.Code == "KS021");
        var sw = Assert.IsType<KsSwitch>(ast.Body[0]);
        Assert.Equal(2, sw.Arms.Length);
        // Arm 0's first statement has no leading comment; the between-arm comments
        // attach to the next arm's first statement, in source order.
        Assert.Null(sw.Arms[0][0].LeadingComment);
        Assert.Equal("between arms", sw.Arms[1][0].LeadingComment);
        Assert.Equal("before default", sw.Default[0].LeadingComment);
    }

    [Fact]
    public void Error_KS040_Assignment_Missing_Variable_Name()
    {
        // Pipeline ending with "=" but no identifier follows.
        var src = "var {\n    int x\n}\n1 > x =\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS040");
    }

    [Fact]
    public void Error_KS041_Segment_Missing_Name()
    {
        // ">" at end of line with no identifier following.
        var src = "1 >\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS041");
    }

    [Fact]
    public void Error_KS050_Unexpected_Token_In_Expression()
    {
        // "@" is not in the grammar alphabet → unexpected token in expression.
        var src = "if @\n    Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS050");
    }

    [Fact]
    public void Error_KS052_Segment_Call_Unterminated()
    {
        // Call in expression position (if condition) missing closing ")".
        // ParseSegment uses KS042; ParseExpression call branch uses KS052.
        var src = "if Foo(\n    Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS052");
    }

    [Fact]
    public void Error_KS060_Multi_Source_Condition_Missing_Pipe()
    {
        // Multiple sources in condition but no ">" pipeline segment.
        var src = "if a, b\n    Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS060");
    }

    [Fact]
    public void Error_KS061_ForEach_In_Condition_Pipeline()
    {
        // forEach is not valid inside a condition pipeline.
        var src = "if 1 > forEach as i\n    Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS061");
    }

    [Fact]
    public void Error_KS063_Missing_Colon_After_ControlFlow_Header()
    {
        // The ':' terminator is now mandatory on control-flow headers (Python-style).
        var src = "if cond\n    Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS063");
    }

    [Fact]
    public void Valid_Colon_Header_No_Diagnostic()
    {
        var src = """
            if cond:
                Print("x")
            """;
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.False(diag.HasErrors);
    }

    [Fact]
    public void Error_KS064_Else_If_Rejected()
    {
        // `else if` is not supported — the renderer always emits nested form and the
        // parser rejects the sugar (bijection guarantee: else-if and nested if map to
        // the same IR, which would break Get-Get idempotence).
        var src = """
            if c:
                Print("then")
            else if c2:
                Print("else")
            """;
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS064");
    }

    [Fact]
    public void Error_KS070_Dict_Key_Missing_Colon()
    {
        // Dict key must be followed by ':'.
        var src = "var {\n    dict d = {a 1}\n}\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS070");
    }

    [Fact]
    public void Error_KS071_Dict_Missing_Comma_Or_Brace()
    {
        // After a key-value pair, the dict literal must continue with ',' or close with '}'.
        var src = "var {\n    dict d = {a: 1 b: 2}\n}\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS071");
    }

    [Fact]
    public void Error_KS072_Dict_Key_Must_Be_String_Or_Identifier()
    {
        // Dict keys are string literals or identifiers only — an integer key is illegal.
        var src = "var {\n    dict d = {1: 2}\n}\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS072");
    }

    [Fact]
    public void Error_KS073_Nested_Dict_Rejected()
    {
        // Dict values are flat scalars only — nested dicts are rejected (use JSON format).
        var src = "var {\n    dict d = {a: {b: 1}}\n}\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS073");
    }

    [Fact]
    public void Error_KS074_Dict_Value_Not_Scalar()
    {
        // Dict values must be scalar literals — the placeholder `_` is not a value.
        var src = "var {\n    dict d = {a: _}\n}\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS074");
    }

    [Fact]
    public void Error_KS075_Parenthesised_Source_Missing_Close()
    {
        // Parenthesised pipeline source `(a > Func` must be closed with ')'.
        var src = "(1 > Add(_, 1) > Print\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS075");
    }

    [Fact]
    public void Error_KS076_Decl_Initialiser_Must_Be_Literal()
    {
        // Decl-block initialisers are literal-only — references/expressions are illegal
        // (a BP definition node can only carry a payload, not data edges).
        var src = """
            const {
                int MAX = 99
            }
            var {
                int x = MAX
            }
            """;
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS076");
    }

    [Fact]
    public void Error_KS078_Deeply_Nested_Parens_Reported_Not_StackOverflow()
    {
        // B5c: expression nesting is depth-capped — 300 parenthesised sources used to
        // recurse ParseExpression 300 levels deep (uncatchable StackOverflowException
        // on extreme input). Must now report KS078 and return a partial program.
        var depth = 300;
        var src = "if " + new string('(', depth) + "1" + new string(')', depth) + ":\n    Print(\"x\")\n";
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS078");
    }

    [Fact]
    public void Error_KS078_Deeply_Nested_If_Bodies_Reported_Not_StackOverflow()
    {
        // B5c: statement-body nesting is depth-capped the same way — 300 nested if
        // bodies used to recurse ParseBody 300 levels deep.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 300; i++)
            sb.Append(' ', i * 4).Append("if c:\n");
        sb.Append(' ', 300 * 4).Append("Print(\"x\")\n");
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(sb.ToString());
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS078");
    }

    [Fact]
    public void Parse_String_Escape_Codes_Decoded()
    {
        // Tokenizer decodes C#-style escapes in string literals (payload = decoded text).
        var src = "Print(\"a\\nb\\tc\\\\d\\\"e\\'f\\0g\")\n";
        var ir = _fixture.KsLens.Parse(src, []);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        var call = Assert.IsType<KsCall>(pipe.Sources[0]);
        var lit = Assert.IsType<KsLiteral>(call.Args[0]);
        Assert.Equal(KsLiteralKind.String, lit.Kind);
        Assert.Equal("a\nb\tc\\d\"e'f\0g", lit.Value);
    }

    [Fact]
    public void Parse_Unknown_Escape_Passes_Through_Verbatim()
    {
        // Unknown escapes (e.g. \x) pass through verbatim per the tokenizer contract.
        var src = "Print(\"\\x\")\n";
        var ir = _fixture.KsLens.Parse(src, []);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        var call = Assert.IsType<KsCall>(pipe.Sources[0]);
        var lit = Assert.IsType<KsLiteral>(call.Args[0]);
        Assert.Equal("x", lit.Value);
    }

    [Fact]
    public void Multiline_Condition_With_Segment_Comment_Trip()
    {
        // Multi-line condition with intermediate + last segment comments round-trips.
        // Intermediate segment comment on its continuation line; last segment comment
        // after the ':' on the last continuation line. Continuation lines and the
        // body's first line share the header+1 indent (the `>` prefix distinguishes
        // them — the parser anchors the body to the KEYWORD's indent, not the last
        // continuation line's).
        var src = """
            var {
                int a
                int b
            }
            if a, b
                > Add(_, 1) // step one
                > Compare("BEQ"): // equality check
                Print("yes")
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var rendered = _fixture.KsLens.Project(ir1);
        // Multi-line condition rendered (intermediate segment has a comment).
        Assert.Contains("> Add(_, 1) // step one", rendered);
        Assert.Contains("> Compare(\"BEQ\"):", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    // ── Multi-line pipeline ──

    [Fact]
    public void Parse_Multiline_Pipeline_Two_Segments()
    {
        var src = """
            1, 2
                > Add
                > Print
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Single(ir.Body);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Equal(2, pipe.Sources.Length);
        Assert.Equal(2, pipe.Segments.Length);
    }

    [Fact]
    public void Parse_Multiline_Equal_To_Single_Line()
    {
        var multiLine = _fixture.KsLens.Parse("""
            1, 2
                > Add
                > Print
            """, []);
        var singleLine = _fixture.KsLens.Parse("1, 2 > Add > Print\n", []);
        Assert.Equal(singleLine, multiLine);
    }

    [Fact]
    public void Parse_Multiline_No_Segment_On_First_Line()
    {
        var src = """
            1, 2
                > Add
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Single(ir.Body);
        var pipe = Assert.IsType<PipelineStatement>(ir.Body[0]);
        Assert.Equal(2, pipe.Sources.Length);
        Assert.Single(pipe.Segments);
    }

    [Fact]
    public void Parse_Multiline_In_If_Body()
    {
        var src = """
            if 1, 1 > Compare("BEQ"):
                1, 2
                    > Add
                    > Print
                Print("no")
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var iff = Assert.IsType<IfStatement>(ir.Body[0]);
        Assert.Equal(2, iff.ThenBody.Length);
        // First statement is a multi-line pipeline 1,2 > Add > Print
        var pipe1 = Assert.IsType<PipelineStatement>(iff.ThenBody[0]);
        Assert.Equal(2, pipe1.Segments.Length);
        // Second statement is a single-line bare call Print("no")
        var pipe2 = Assert.IsType<PipelineStatement>(iff.ThenBody[1]);
        Assert.Empty(pipe2.Segments);
        Assert.Single(pipe2.Sources);
    }

    // ── KS053: bare statement rejection (single identifier/literal now legal no-op) ──

    [Fact]
    public void Parse_Bare_Literal_Is_Now_A_NoOp_Statement()
    {
        // 2026-08-03: a single literal line is a legal no-op exec anchor (the BP-side
        // counterpart of a usage node on the exec chain without data edges).
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics("0\n");
        Assert.DoesNotContain(diag.Items, d => d.Code == "KS053");
        Assert.Single(ast.Body);
    }

    [Fact]
    public void Parse_Bare_Identifier_Is_Now_A_NoOp_Statement()
    {
        // 2026-08-03: a single identifier line is a legal no-op exec anchor.
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics("counter\n");
        Assert.DoesNotContain(diag.Items, d => d.Code == "KS053");
        Assert.Single(ast.Body);
    }

    [Fact]
    public void Parse_Bare_MultiSource_List_Still_Rejected_With_KS053()
    {
        // Multi-source bare lists (no segments) remain invalid.
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics("a, b\n");
        Assert.Contains(diag.Items, d => d.Code == "KS053");
    }

    [Fact]
    public void Parse_Bare_Call_Still_Legal_No_KS053()
    {
        // Bare call (single KsCall source, no segments) is the Print("hello") form —
        // it must remain legal and not trigger KS053.
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics("Print(\"hello\")\n");
        Assert.DoesNotContain(diag.Items, d => d.Code == "KS053");
    }

    [Fact]
    public void Parse_Pipeline_Assignment_Still_Legal_No_KS053()
    {
        // `0 > counter` is a pipeline assignment — must not trigger KS053.
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics("var {\n    int counter\n}\n0 > counter\n");
        Assert.DoesNotContain(diag.Items, d => d.Code == "KS053");
    }

    // ── T7: decl-block comment system (block doc / row leading / row trailing / file-end) ──

    [Fact]
    public void DeclBlock_Leading_Comment_Maps_To_Const_LeadingComment()
    {
        // Block-preceding comment run becomes the block's doc comment; a comment run
        // directly above a declaration row becomes that row's LeadingComment.
        var src = """
            // block doc line
            const {
                // row comment
                int x = 5
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Equal("block doc line", ir.ConstantsDocComment);
        Assert.Equal("row comment", ir.Constants["x"].LeadingComment);
        Assert.Null(ir.Constants["x"].TrailingComment);

        // Comment run between the const block and the var block goes to the var block doc.
        var src2 = """
            const {
                int x = 5
            }
            // var block doc
            var {
                // var row comment
                int counter // counter note
            }
            """;
        var ir2 = _fixture.KsLens.Parse(src2, []);
        Assert.Null(ir2.ConstantsDocComment);
        Assert.Equal("var block doc", ir2.GlobalVarsDocComment);
        Assert.Equal("var row comment", ir2.GlobalVars["counter"].LeadingComment);
        Assert.Equal("counter note", ir2.GlobalVars["counter"].TrailingComment);
    }

    [Fact]
    public void DeclBlock_Trailing_Comment_Maps_To_Node_Comment()
    {
        // An inline comment on a declaration row is its TrailingComment — and must NOT
        // trigger KS076 (previously the comment was misread as a trailing expression).
        var src = """
            const {
                int x = 5 // note
            }
            """;
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.False(diag.HasErrors);
        Assert.DoesNotContain(diag.Items, d => d.Code == "KS076");
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Equal("note", ir.Constants["x"].TrailingComment);
    }

    [Fact]
    public void DeclBlock_Comment_Projects_To_Definition_Node()
    {
        var src = """
            const {
                // leading note
                int x = 5 // trailing note
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var defNode = Assert.Single(bp.Nodes.OfType<ConstNode>(), n => n.ConstName == "x" && n.IsDefinition);
        Assert.Equal("trailing note", defNode.Comment);
        var gc = Assert.Single(bp.GroupComments, g => g.AnchorNodeId == defNode.Id);
        Assert.Equal("leading note", gc.Comment);
        Assert.Single(gc.NodeIds);
        Assert.Equal(defNode.Id, gc.NodeIds[0]);
    }

    [Fact]
    public void DeclBlock_Comment_Reverse_Restores()
    {
        var src = """
            const {
                // leading note
                int x = 5 // trailing note
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Equal("leading note", reversed.Constants["x"].LeadingComment);
        Assert.Equal("trailing note", reversed.Constants["x"].TrailingComment);
        Assert.Equal(ir.Constants["x"], reversed.Constants["x"]);
    }

    [Fact]
    public void DeclBlock_Doc_Not_Projected_To_BP()
    {
        var src = """
            // block doc
            const {
                int x = 5
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        Assert.DoesNotContain(bp.GroupComments, g => g.Comment.Contains("block doc"));
        Assert.DoesNotContain(bp.Nodes, n => n.Comment is { Length: > 0 } && n.Comment.Contains("block doc"));
    }

    [Fact]
    public void DeclBlock_Doc_RoundTrip()
    {
        var src = """
            // first doc line
            // second doc line
            const {
                int x = 5
            }
            // file end note
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        Assert.Equal("first doc line\nsecond doc line", ir1.ConstantsDocComment);
        Assert.Equal("file end note", ir1.TrailingDocComment);
        var rendered = _fixture.KsLens.Project(ir1);
        Assert.Contains("// first doc line", rendered);
        Assert.Contains("// second doc line", rendered);
        Assert.Contains("// file end note", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
        Assert.Equal("first doc line\nsecond doc line", ir2.ConstantsDocComment);
        Assert.Equal("file end note", ir2.TrailingDocComment);
    }

    [Fact]
    public void Trailing_File_End_Comment_Preserved()
    {
        var src = """
            Print("done")
            // file end comment
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        Assert.Equal("file end comment", ir1.TrailingDocComment);
        var rendered = _fixture.KsLens.Project(ir1);
        Assert.Contains("// file end comment", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
        Assert.Equal("file end comment", ir2.TrailingDocComment);
    }

    [Fact]
    public void File_Only_Comments_Go_To_TrailingDoc()
    {
        // A comment-only file has no statements and no decl blocks — everything lands
        // in TrailingDocComment.
        var src = """
            // only a comment
            // and another
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Empty(ir.Body);
        Assert.Empty(ir.Constants);
        Assert.Equal("only a comment\nand another", ir.TrailingDocComment);
        var rendered = _fixture.KsLens.Project(ir);
        Assert.Contains("// only a comment", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir, ir2);
        Assert.Equal("only a comment\nand another", ir2.TrailingDocComment);
    }

    [Fact]
    public void DeclBlock_Free_Comment_Inside_Block_Joins_Doc()
    {
        // A free-floating comment inside the block (block tail, not leading any row)
        // folds into the block doc; on re-render it is normalised to the block front
        // (content preserved).
        var src = """
            const {
                int x = 5
                // free floating at block tail
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Equal("free floating at block tail", ir.ConstantsDocComment);
        Assert.Null(ir.Constants["x"].LeadingComment);
        var rendered = _fixture.KsLens.Project(ir);
        Assert.Matches("(?s)// free floating at block tail.*const \\{" , rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir, ir2);
        Assert.Equal("free floating at block tail", ir2.ConstantsDocComment);
    }

    [Fact]
    public void DeclBlock_No_KS012_For_Comment_Lines()
    {
        // Comment lines inside a decl block must not be parsed as declarations (which
        // previously emitted 2x KS012 and, for identical texts, crashed the lowerer
        // with a duplicate dictionary key).
        var src = """
            const {
                // shared note
                int x = 5
                // shared note
                int y = 6
            }
            """;
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.DoesNotContain(diag.Items, d => d.Code == "KS012");
        // Lowering must not throw (identical comment texts used to collide on the
        // constants dictionary key).
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Equal("shared note", ir.Constants["x"].LeadingComment);
        Assert.Equal("shared note", ir.Constants["y"].LeadingComment);
    }

    [Fact]
    public void DeclBlock_Dict_Init_With_Comments()
    {
        // A dict-initialised row with an inline comment coexists with a block doc.
        // (String-literal keys keep the rendered form identical to the source form —
        // identifier keys are canonicalised to quoted strings by the renderer.)
        var src = """
            // dict const doc
            const {
                dict config = {"a": 1} // inline note
            }
            """;
        var (ast, diag) = _fixture.KsLens.ParseAstWithDiagnostics(src);
        Assert.False(diag.HasErrors);
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Equal("dict const doc", ir.ConstantsDocComment);
        Assert.Equal("inline note", ir.Constants["config"].TrailingComment);
        var rendered = _fixture.KsLens.Project(ir);
        Assert.Contains("// dict const doc", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir, ir2);
    }

    [Fact]
    public void DeclBlock_Empty_Block_Doc_Preserved()
    {
        // An empty block with a preceding comment keeps its doc (renders as an empty
        // block so the comment cannot drift onto the next statement / file end).
        var src = """
            // empty block doc
            const {
            }
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        Assert.Empty(ir.Constants);
        Assert.Equal("empty block doc", ir.ConstantsDocComment);
        var rendered = _fixture.KsLens.Project(ir);
        Assert.Contains("const {", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir, ir2);
        Assert.Equal("empty block doc", ir2.ConstantsDocComment);
    }

    [Fact]
    public void DeclBlock_Comment_Row_RoundTrip_Through_BP()
    {
        // Full BP round-trip: KS -> IR -> BP -> IR -> KS -> IR keeps both comment kinds.
        var src = """
            // block doc
            const {
                // row leading
                int x = 5 // row trailing
            }
            var {
                int counter // var note
            }
            Print("start")
            """;
        var ir1 = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir1);
        var mid = _fixture.BpLens.Reverse(bp);
        Assert.Equal("row leading", mid.Constants["x"].LeadingComment);
        Assert.Equal("row trailing", mid.Constants["x"].TrailingComment);
        Assert.Equal("var note", mid.GlobalVars["counter"].TrailingComment);
        var rendered = _fixture.KsLens.Project(mid);
        Assert.Contains("// row leading", rendered);
        Assert.Contains("// row trailing", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
        Assert.Equal("row leading", ir2.Constants["x"].LeadingComment);
        Assert.Equal("row trailing", ir2.Constants["x"].TrailingComment);
        Assert.Equal("var note", ir2.GlobalVars["counter"].TrailingComment);
    }

    [Fact]
    public void DeclBlock_Doc_Survives_Bp_RoundTrip_With_Baseline()
    {
        // BP does NOT project KS-side privileged doc comments (block doc / file-end),
        // so a full reversal rebuilds the IR without them — the caller must re-attach
        // them from the pre-reversal IR via ReverseWithNodePaths' ksPrivileged parameter
        // (same pattern as the helper functions re-attachment).
        var src = """
            // const block doc
            const {
                int x = 5
            }
            // file end note
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var (reversed, _) = _fixture.BpLens.ReverseWithNodePaths(bp, [], ir);
        Assert.Equal("const block doc", reversed.ConstantsDocComment);
        Assert.Equal("file end note", reversed.TrailingDocComment);
        var rendered = _fixture.KsLens.Project(reversed);
        Assert.Contains("// const block doc", rendered);
        Assert.Contains("// file end note", rendered);
        var ir2 = _fixture.KsLens.Parse(rendered, []);
        Assert.Equal(ir, ir2);
    }

    [Fact]
    public void DeclBlock_Doc_Dropped_Without_Baseline()
    {
        // Backward compatibility: the no-baseline overload keeps the previous behaviour
        // (privileged doc fields are lost on the BP round-trip) — callers must opt in
        // by passing the pre-reversal IR.
        var src = """
            // const block doc
            const {
                int x = 5
            }
            // file end note
            """;
        var ir = _fixture.KsLens.Parse(src, []);
        var bp = _fixture.BpLens.Project(ir);
        var reversed = _fixture.BpLens.Reverse(bp);
        Assert.Null(reversed.ConstantsDocComment);
        Assert.Null(reversed.TrailingDocComment);
    }
}