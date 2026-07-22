// ─────────────────────────────────────────────────────────────────────────────
// Phase 2 acceptance tests for BsTextLens (KS indented parser + lowerer + renderer).
//
// Covers the KS text round-trip pipeline:
//   • Parse empty program
//   • Parse const/var blocks
//   • Parse if/else, forEach, while, switch, nested control flow
//   • Parse break/continue/exit
//   • Parse pipelines (bare call, multi-segment, with assignment tap)
//   • Reject Tab characters in indentation (§十二-A)
//   • Report indent errors with line/column
//   • Round-trip idempotence: parse → render → parse ≡ id (modulo formatting)
//   • Comments are preserved through round-trip
// ─────────────────────────────────────────────────────────────────────────────

using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Ast;
using KitX.WorkflowV6.Ir.Statements;
using KitX.WorkflowV6.Lens.BsTextLens;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

public class BsTextLensTests
{
    private readonly BsTextLens _lens = new(new BuiltinFunctionRegistry());

    // ── Parse empty program ──

    [Fact]
    public void Parse_Empty_Program()
    {
        var ir = _lens.Parse("", []);
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
        var ir = _lens.Parse(src, []);
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
            if cond
                Print("then")
            else
                Print("else")
            """;
        var ir = _lens.Parse(src, []);
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
            forEach Range(0, 10, 1) as i
                i > Print
            """;
        var ir = _lens.Parse(src, []);
        Assert.Single(ir.Body);
        var fe = Assert.IsType<ForEachStatement>(ir.Body[0]);
        Assert.Equal("i", fe.ItemName);
        Assert.Single(fe.Body);
        Assert.IsType<PipelineStatement>(fe.Body[0]);
    }

    [Fact]
    public void Parse_ForEach_Pipeline_Form()
    {
        // The §4.3 example form: Range(0, loopMax, 1) > forEach as i.
        // Should desugar to the same ForEachStatement as the prefix form.
        var src = """
            Range(0, 10, 1) > forEach as i
                i > Print
            """;
        var ir = _lens.Parse(src, []);
        Assert.Single(ir.Body);
        var fe = Assert.IsType<ForEachStatement>(ir.Body[0]);
        Assert.Equal("i", fe.ItemName);
        Assert.Single(fe.Body);
        Assert.IsType<PipelineStatement>(fe.Body[0]);
    }

    // ── Parse while ──

    [Fact]
    public void Parse_While()
    {
        var src = """
            while cond
                Print("body")
            """;
        var ir = _lens.Parse(src, []);
        Assert.Single(ir.Body);
        var ws = Assert.IsType<WhileStatement>(ir.Body[0]);
        Assert.Single(ws.Body);
    }

    // ── Parse nested if ──

    [Fact]
    public void Parse_Nested_If()
    {
        var src = """
            if outer
                Print("outer then")
                if inner
                    Print("inner then")
            """;
        var ir = _lens.Parse(src, []);
        Assert.Single(ir.Body);
        var outer = Assert.IsType<IfStatement>(ir.Body[0]);
        Assert.Equal(2, outer.ThenBody.Length);
        Assert.IsType<IfStatement>(outer.ThenBody[1]);
    }

    // ── Parse break/continue/exit ──

    [Fact]
    public void Parse_Loop_Control_Statements()
    {
        var src = """
            forEach Range(0, 5, 1) as i
                break
                continue
                exit
            """;
        var ir = _lens.Parse(src, []);
        var fe = Assert.IsType<ForEachStatement>(ir.Body[0]);
        Assert.Equal(3, fe.Body.Length);
        Assert.IsType<BreakStatement>(fe.Body[0]);
        Assert.IsType<ContinueStatement>(fe.Body[1]);
        Assert.IsType<ExitStatement>(fe.Body[2]);
    }

    // ── Tab rejected ──

    [Fact]
    public void Parse_Tab_Rejected()
    {
        var src = "if cond\n\tPrint(\"x\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS001");
    }

    // ── Indent error ──

    [Fact]
    public void Parse_Indent_Error_Mismatch()
    {
        // 3-space indent is not a multiple of 4 — must report BS002.
        var src = "if cond\n   Print(\"x\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS002");
    }

    // ── Round-trip idempotence ──

    [Fact]
    public void RoundTrip_Idempotent_Simple_Pipeline()
    {
        var src = "Print(\"hello\")\n";
        var ir1 = _lens.Parse(src, []);
        var rendered = _lens.Project(ir1);
        var ir2 = _lens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Idempotent_If_Else()
    {
        var src = """
            if cond
                Print("then")
            else
                Print("else")
            """;
        var ir1 = _lens.Parse(src, []);
        var rendered = _lens.Project(ir1);
        var ir2 = _lens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Idempotent_ForEach()
    {
        var src = """
            forEach Range(0, 10, 1) as i
                i > Print
            """;
        var ir1 = _lens.Parse(src, []);
        var rendered = _lens.Project(ir1);
        var ir2 = _lens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Idempotent_Nested_If()
    {
        var src = """
            if outer
                Print("outer then")
                if inner
                    Print("inner then")
            """;
        var ir1 = _lens.Parse(src, []);
        var rendered = _lens.Project(ir1);
        var ir2 = _lens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Idempotent_While_With_Break()
    {
        var src = """
            while cond
                Print("body")
                break
            """;
        var ir1 = _lens.Parse(src, []);
        var rendered = _lens.Project(ir1);
        var ir2 = _lens.Parse(rendered, []);
        Assert.Equal(ir1, ir2);
    }

    // ── Comments round-trip (v5.1 §9 three-form anchoring: line comments preserved) ──

    [Fact]
    public void Parse_Inline_Comment_Ignored()
    {
        // An inline `//` comment terminates a line's tokens but doesn't affect parsing.
        var src = "Print(\"x\") // this is a comment\n";
        var ir = _lens.Parse(src, []);
        Assert.Single(ir.Body);
        Assert.IsType<PipelineStatement>(ir.Body[0]);
    }

    [Fact]
    public void Parse_Full_Line_Comment_Ignored()
    {
        // A full-line comment line is dropped entirely (no Indent emitted).
        var src = """
            // this is a full-line comment
            Print("x")
            """;
        var ir = _lens.Parse(src, []);
        Assert.Single(ir.Body);  // only the Print statement
    }

    // ── Project renders correct indentation ──

    [Fact]
    public void Project_Renders_Correct_Indentation()
    {
        var src = """
            if cond
                Print("then")
            """;
        var ir = _lens.Parse(src, []);
        var rendered = _lens.Project(ir);
        // The then body must be indented by 4 spaces.
        Assert.Contains("\n    Print(\"then\")", rendered);
    }

    [Fact]
    public void Project_Renders_Nested_Indentation()
    {
        var src = """
            if outer
                if inner
                    Print("deep")
            """;
        var ir = _lens.Parse(src, []);
        var rendered = _lens.Project(ir);
        // The innermost Print must be indented by 8 spaces.
        Assert.Contains("\n        Print(\"deep\")", rendered);
    }

    // ── ParseAst produces BsProgram ──

    [Fact]
    public void ParseAst_Returns_BsProgram()
    {
        var src = "Print(\"x\")\n";
        var ast = _lens.ParseAst(src);
        var program = Assert.IsType<BsProgram>(ast);
        Assert.Single(program.Body);
        Assert.IsType<BsPipeline>(program.Body[0]);
    }

    // ── Error scenario coverage (KS0xx codes) ──

    [Fact]
    public void Error_BS051_Identifier_In_Function_Parens()
    {
        // v6.0 rule: function parens may only contain literals/placeholders.
        // `Print(myVar)` — myVar is an identifier inside parens → BS051.
        var src = "Print(myVar)\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS051");
    }

    [Fact]
    public void Error_BS051_Identifier_In_Segment_Parens()
    {
        // `1 > HelperFuncAdd(x, _)` — x is an identifier inside segment parens → BS051.
        var src = "1 > HelperFuncAdd(x, _)\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS051");
    }

    [Fact]
    public void Error_BS051_Not_Raised_For_Literal_Args()
    {
        // `Print("hello")` — all-literal args → no BS051.
        var src = "Print(\"hello\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.False(diag.HasErrors);
    }

    [Fact]
    public void Error_BS051_Not_Raised_For_Placeholder()
    {
        // `1 > Range(0, _, 1)` — _ is a placeholder, not an identifier → no BS051.
        var src = "1 > Range(0, _, 1)\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.False(diag.HasErrors);
    }

    [Fact]
    public void Error_BS030_Missing_As_After_ForEach()
    {
        var src = "forEach Range(0, 3, 1)\n    Print(\"x\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS030");
    }

    [Fact]
    public void Error_BS042_Unterminated_Call_Args()
    {
        var src = "Print(\"hello\"\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS042" || d.Code == "KS052");
    }

    [Fact]
    public void Error_BS062_Empty_If_Body()
    {
        var src = "if cond\nPrint(\"not indented\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS062");
    }

    [Fact]
    public void Error_BS010_Top_Level_Not_Indent_Zero()
    {
        // Statement at indent 2 (not 0) at top level.
        var src = "    Print(\"x\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS010");
    }

    [Fact]
    public void Error_BS011_Duplicate_Const_Block()
    {
        var src = """
            const {
                int a = 1
            }

            const {
                int b = 2
            }
            """;
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS011");
    }

    [Fact]
    public void Error_Collection_Contains_All_Error_Codes()
    {
        // Multiple errors in one source — all should be collected (error recovery).
        var src = "Print(myVar)\nPrint(otherVar)\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        // Both lines should produce BS051.
        var bs051Count = diag.Items.Count(d => d.Code == "KS051");
        Assert.True(bs051Count >= 2, $"Expected >=2 BS051 errors, got {bs051Count}");
    }

    // ── Systematic KS0xx error code coverage (remaining 11 codes) ──

    [Fact]
    public void Error_KS012_Declaration_Missing_Type_Name()
    {
        // const row missing type identifier: "5" is IntegerLiteral, not Identifier.
        var src = "const {\n    5\n}\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS012");
    }

    [Fact]
    public void Error_KS013_Missing_LBrace_After_Const()
    {
        // "const int x = 5" — const followed by identifier, not "{".
        var src = "const int x = 5\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS013");
    }

    [Fact]
    public void Error_KS020_Switch_Case_Missing_Colon()
    {
        // Case label without ":" separator.
        var src = "switch sel\n    0 Print(\"zero\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS020");
    }

    [Fact]
    public void Error_KS021_Switch_Arm_Invalid_Label()
    {
        // Arm label must be integer or "default"; "x" is an identifier.
        var src = "switch sel\n    x:\n        Print(\"zero\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS021");
    }

    [Fact]
    public void Error_KS022_Duplicate_Default_Arm()
    {
        var src = "switch sel\n    default:\n        Print(\"a\")\n    default:\n        Print(\"b\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS022");
    }

    [Fact]
    public void Error_KS040_Assignment_Missing_Variable_Name()
    {
        // Pipeline ending with "=" but no identifier follows.
        var src = "var {\n    int x\n}\n1 > x =\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS040");
    }

    [Fact]
    public void Error_KS041_Segment_Missing_Name()
    {
        // ">" at end of line with no identifier following.
        var src = "1 >\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS041");
    }

    [Fact]
    public void Error_KS050_Unexpected_Token_In_Expression()
    {
        // "@" is not in the grammar alphabet → unexpected token in expression.
        var src = "if @\n    Print(\"x\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS050");
    }

    [Fact]
    public void Error_KS052_Segment_Call_Unterminated()
    {
        // Call in expression position (if condition) missing closing ")".
        // ParseSegment uses KS042; ParseExpression call branch uses KS052.
        var src = "if Foo(\n    Print(\"x\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS052");
    }

    [Fact]
    public void Error_KS060_Multi_Source_Condition_Missing_Pipe()
    {
        // Multiple sources in condition but no ">" pipeline segment.
        var src = "if a, b\n    Print(\"x\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS060");
    }

    [Fact]
    public void Error_KS061_ForEach_In_Condition_Pipeline()
    {
        // forEach is not valid inside a condition pipeline.
        var src = "if 1 > forEach as i\n    Print(\"x\")\n";
        var (ast, diag) = _lens.ParseAstWithDiagnostics(src);
        Assert.True(diag.HasErrors);
        Assert.Contains(diag.Items, d => d.Code == "KS061");
    }
}