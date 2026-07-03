using KitX.Workflow.Builtin;
using KitX.Workflow.Ir.Ast;
using KitX.Workflow.Ir.Lowering;
using KitX.Workflow.Lens.BsTextLens;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// BsParserTests — BS text → BlockScript AST parse + diagnostics coverage.
//
// Adapted from KitX.Workflow.Test.Xunit.ParseAndDiagnosticsTests (the T01–T18
// parse/structure tests and the T54–T60 diagnostics tests), retargeted at the
// new BsTextLensParser. The execution-style tests (T19–T28, which need a
// compiling/running executor) are out of scope for the text-lens layer and are
// intentionally dropped.
//
// Each test exercises a distinct BS construct: literal expression, pipeline,
// control-flow bare call, ForLoop, Switch, ConstBlock/PubVarBlock declarations,
// comment retention, and the v5.0 error diagnostics (illegal `=`, nested call,
// unterminated block, ConstBlock/ForLoop-index write, reserved `_`, dead code).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Verifies the migrated BS text lens: Superpower tokenizer + combinator parser
/// produce the correct immutable BlockScript AST, and the v5.0 semantic
/// diagnostics are emitted with the expected codes.
/// </summary>
public class BsParserTests
{
    // Discover once per fixture. The descriptors live in the main IR assembly.
    private static BsTextLensParser BuildParser()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        return new BsTextLensParser(registry);
    }

    // A minimal terminating tail appended to scripts so every block ends in a
    // control-flow statement (§7.7), mirroring the legacy TestData.End.
    private const string End = "\n\n#Block End\nPrint(\"done\");\nExit();";

    private static bool HasDiag(BsTextLensParseResult pr, string code)
        => pr.Diagnostics.Any(d => d.Code == code);

    // ── Literal / pipeline expression parsing ──

    [Fact]
    public void Parse_StringLiteralPipeline_ProducesBSPipeline()
    {
        var pr = BuildParser().Parse("#MainBlock\n\"hello\" > Print;\nGoto(\"End\");" + End);
        Assert.True(pr.IsSuccess, "string literal pipeline should parse");

        // The first statement is the pipeline carrying the literal source.
        var main = pr.Script!.MainBlock!;
        var first = main.Statements[0];
        var es = Assert.IsType<ExpressionStatement>(first);
        var pipe = Assert.IsType<BSPipeline>(es.ParsedExpression);
        Assert.Single(pipe.Sources);
        Assert.Equal("\"hello\"", pipe.Sources[0].SourceText);
        Assert.Single(pipe.Targets);
        Assert.Equal("Print", pipe.Targets[0].MethodName);
    }

    [Fact]
    public void Parse_NumericAndBooleanLiterals_TypedCorrectly()
    {
        var pr = BuildParser().Parse("#MainBlock\n42 > Print;\n3.14 > Print;\ntrue > Print;\nGoto(\"End\");" + End);
        Assert.True(pr.IsSuccess);

        var stmts = pr.Script!.MainBlock!.Statements;
        var intLit = (BSLiteral)((BSPipeline)((ExpressionStatement)stmts[0]).ParsedExpression!).Sources[0];
        var dblLit = (BSLiteral)((BSPipeline)((ExpressionStatement)stmts[1]).ParsedExpression!).Sources[0];
        var boolLit = (BSLiteral)((BSPipeline)((ExpressionStatement)stmts[2]).ParsedExpression!).Sources[0];

        Assert.Equal(BSLiteralKind.Integer, intLit.Kind);
        Assert.Equal(42, intLit.Value);
        Assert.Equal(BSLiteralKind.Double, dblLit.Kind);
        Assert.Equal(3.14, dblLit.Value);
        Assert.Equal(BSLiteralKind.Boolean, boolLit.Kind);
        Assert.Equal(true, boolLit.Value);
    }

    // ── Pipeline `>` syntax (multi-target chain) ──

    [Fact]
    public void Parse_PipelineChain_HasOrderedTargets()
    {
        // a, b > StringConcat > Print  — two sources, two targets.
        var pr = BuildParser().Parse("#ConstBlock\nstring a = \"A\";\nstring b = \"B\";\n\n#MainBlock\na, b > StringConcat > Print;\nGoto(\"End\");" + End);
        Assert.True(pr.IsSuccess);

        var pipe = (BSPipeline)((ExpressionStatement)pr.Script!.MainBlock!.Statements[0]).ParsedExpression!;
        Assert.Equal(2, pipe.Sources.Count);
        Assert.Equal(2, pipe.Targets.Count);
        Assert.Equal("StringConcat", pipe.Targets[0].MethodName);
        Assert.Equal("Print", pipe.Targets[1].MethodName);
    }

    [Fact]
    public void Parse_PassThroughTap_BareIdentifierTarget()
    {
        // 0 > x > Print — the bare `x` target is a variable tap (zero-arg BSCall).
        var pr = BuildParser().Parse("#PubVarBlock\nint x;\n\n#MainBlock\n0 > x > Print;\nGoto(\"End\");" + End);
        Assert.True(pr.IsSuccess);

        var pipe = (BSPipeline)((ExpressionStatement)pr.Script!.MainBlock!.Statements[0]).ParsedExpression!;
        // Tap renders without parens.
        Assert.Equal("x", pipe.Targets[0].SourceText);
        Assert.Empty(pipe.Targets[0].Args);
    }

    // ── Control-flow bare call ──

    [Fact]
    public void Parse_BranchBareCall_BecomesFlowControlStatement()
    {
        var src = "#PubVarBlock\nbool cond;\n\n#MainBlock\ntrue > cond;\nBranch(cond, \"A\", \"B\");\n\n#Block A\nPrint(\"true\");\nGoto(\"End\");\n\n#Block B\nPrint(\"false\");\nGoto(\"End\");" + End;
        var pr = BuildParser().Parse(src);
        Assert.True(pr.IsSuccess);

        var main = pr.Script!.MainBlock!;
        // Second statement is the Branch bare call → FlowControlStatement (registry dispatch).
        var fcs = Assert.IsType<FlowControlStatement>(main.Statements[1]);
        Assert.Equal("Branch", fcs.FunctionName);
    }

    [Fact]
    public void Parse_ForLoop_BareCall_CapturesIndexName()
    {
        var src = "#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, 3, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\nGoto(\"LoopHead\");" + End;
        var pr = BuildParser().Parse(src);
        Assert.True(pr.IsSuccess);

        var loopHead = pr.Script!.NamedBlocks["LoopHead"];
        var fcs = Assert.IsType<FlowControlStatement>(loopHead.Statements[0]);
        Assert.Equal("ForLoop", fcs.FunctionName);
        // FlowArguments[3] is the indexName, already stripped of quotes.
        Assert.Equal("i", fcs.FlowArguments[3]);
        Assert.Equal(2, fcs.Arms.Count);
    }

    [Fact]
    public void Parse_SwitchBareCall_BuildsArmsFromArgs()
    {
        var src = "#MainBlock\nSwitch(0, \"Def\", \"A\", \"B\");\n\n#Block Def\nExit();\n\n#Block A\nExit();\n\n#Block B\nExit();";
        var pr = BuildParser().Parse(src);
        Assert.True(pr.IsSuccess);

        var fcs = Assert.IsType<FlowControlStatement>(pr.Script!.MainBlock!.Statements[0]);
        Assert.Equal("Switch", fcs.FunctionName);
        // Arms: Default, 0, 1
        Assert.Equal(3, fcs.Arms.Count);
        Assert.Equal("Default", fcs.Arms[0].PinName);
        Assert.Equal("Def", fcs.Arms[0].TargetBlockName);
        Assert.Equal("0", fcs.Arms[1].PinName);
        Assert.Equal("A", fcs.Arms[1].TargetBlockName);
    }

    // ── ConstBlock / PubVarBlock declarations ──

    [Fact]
    public void Parse_ConstBlock_DeclarationWithInit()
    {
        var pr = BuildParser().Parse("#ConstBlock\nint x = 5;\n\n#MainBlock\nGoto(\"End\");" + End);
        Assert.True(pr.IsSuccess);

        var cb = pr.Script!.ConstBlock;
        Assert.NotNull(cb);
        Assert.Single(cb!.Variables);
        Assert.Equal("x", cb.Variables[0].Name);
        Assert.Equal("int", cb.Variables[0].Type);
        Assert.Equal(5, cb.Variables[0].DefaultValue);
    }

    [Fact]
    public void Parse_PubVarBlock_StrongTypesPreserved_OptionalInit()
    {
        var pr = BuildParser().Parse("#PubVarBlock\nint currentLoop;\nstring userInput = \"\";\n\n#MainBlock\n0 > currentLoop;\nGoto(\"End\");" + End);
        Assert.True(pr.IsSuccess);

        var pb = pr.Script!.PubVarBlock!;
        Assert.Equal(2, pb.Variables.Count);
        Assert.Equal("int", pb.Variables[0].Type);
        Assert.Equal("string", pb.Variables[1].Type);
        // Optional init: first decl has no default, second has the empty string.
        Assert.Null(pb.Variables[0].DefaultValue);
        Assert.Equal("", pb.Variables[1].DefaultValue);
    }

    [Fact]
    public void Parse_BlockVarsAndBlockBody_SplitCorrectly()
    {
        var src = "#Block ProcessItem\n##BlockVars\nint c = 0;\nstring buf;\n##BlockBody\nc > Print;\nGoto(\"End\");\n\n#MainBlock\nGoto(\"End\");" + End;
        var pr = BuildParser().Parse(src);
        Assert.True(pr.IsSuccess);

        var blk = pr.Script!.NamedBlocks["ProcessItem"];
        Assert.Equal(2, blk.BlockVars.Count);
        Assert.True(blk.Statements.Count >= 2); // the c > Print pipeline + the Goto flow control
        Assert.True(blk.HasExplicitBlockBody);
    }

    // ── Comment retention (v5.1) ──

    [Fact]
    public void Parse_StatementAboveComment_AttachedToNextStatement()
    {
        var src = "#MainBlock\n// greet the user\n\"hi\" > Print;\nGoto(\"End\");" + End;
        var pr = BuildParser().Parse(src);
        Assert.True(pr.IsSuccess);

        var first = pr.Script!.MainBlock!.Statements[0];
        Assert.Equal("greet the user", first.Comment);
    }

    // ── Multi-block composition ──

    [Fact]
    public void Parse_MultiBlockComposition_AllBlocksCollected()
    {
        var src = "#ConstBlock\nint a = 1;\n\n#PubVarBlock\nint x;\n\n#MainBlock\nGoto(\"B1\");\n\n#Block B1\nGoto(\"B2\");\n\n#Block B2\nPrint(\"done\");\nExit();";
        var pr = BuildParser().Parse(src);
        Assert.True(pr.IsSuccess);
        // ConstBlock + PubVarBlock + MainBlock + B1 + B2 = 5 blocks.
        Assert.Equal(5, pr.Script!.AllBlocks.Count);
        Assert.NotNull(pr.Script.ConstBlock);
        Assert.NotNull(pr.Script.PubVarBlock);
        Assert.Equal(2, pr.Script.NamedBlocks.Count);
    }

    // ── Error diagnostics ──

    [Fact]
    public void Diag_NestedCall_EmitsBS_NESTED_CALL()
    {
        var pr = BuildParser().Parse("#MainBlock\nPrint(Get(\"x\"));\nGoto(\"End\");" + End);
        Assert.True(HasDiag(pr, "BS_NESTED_CALL"), "nested call should emit BS_NESTED_CALL");
    }

    [Fact]
    public void Diag_IllegalAssignment_EmitsBS_ILLEGAL_ASSIGNMENT()
    {
        var pr = BuildParser().Parse("#PubVarBlock\nint x;\n\n#MainBlock\nx = 42;\nGoto(\"End\");" + End);
        Assert.True(HasDiag(pr, "BS_ILLEGAL_ASSIGNMENT"), "`=` assignment should emit BS_ILLEGAL_ASSIGNMENT");
    }

    [Fact]
    public void Diag_UnterminatedBlock_EmitsBS_UNTERMINATED_BLOCK()
    {
        var pr = BuildParser().Parse("#MainBlock\nPrint(\"hi\");\n");
        Assert.True(HasDiag(pr, "BS_UNTERMINATED_BLOCK"), "block without control-flow terminator should emit BS_UNTERMINATED_BLOCK");
    }

    [Fact]
    public void Diag_ConstBlockWrite_EmitsBS_CONST_WRITE()
    {
        var pr = BuildParser().Parse("#ConstBlock\nint x = 5;\n\n#MainBlock\n0 > x;\nGoto(\"End\");" + End);
        Assert.True(HasDiag(pr, "BS_CONST_WRITE"), "writing to a ConstBlock var should emit BS_CONST_WRITE");
    }

    [Fact]
    public void Diag_ForLoopIndexWrite_EmitsBS_FORLOOP_INDEX_WRITE()
    {
        var src = "#ConstBlock\nint loopMax = 3;\n\n#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\n5 > i;\nGoto(\"LoopHead\");" + End;
        var pr = BuildParser().Parse(src);
        Assert.True(HasDiag(pr, "BS_FORLOOP_INDEX_WRITE"), "writing to the ForLoop index should emit BS_FORLOOP_INDEX_WRITE");
    }

    [Fact]
    public void Diag_ReservedPlaceholder_EmitsBS_RESERVED_PLACEHOLDER()
    {
        var pr = BuildParser().Parse("#PubVarBlock\nint _;\n\n#MainBlock\n0 > _;\nGoto(\"End\");" + End);
        Assert.True(HasDiag(pr, "BS_RESERVED_PLACEHOLDER"), "`_` as a variable name should emit BS_RESERVED_PLACEHOLDER");
    }

    [Fact]
    public void Diag_DeadCodeAfterControlFlow_EmitsBS_DEAD_CODE()
    {
        var pr = BuildParser().Parse("#MainBlock\nPrint(\"executed\");\nGoto(\"End\");\nPrint(\"dead code\");" + End);
        Assert.True(HasDiag(pr, "BS_DEAD_CODE"), "statement after a control-flow terminator should emit BS_DEAD_CODE warning");
    }

    [Fact]
    public void Diag_MissingMainBlock_FailsWithErrorMessage()
    {
        var src = "#Block SomeBlock\nPrint(\"hi\");\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();";
        var pr = BuildParser().Parse(src);
        Assert.False(pr.IsSuccess);
        Assert.Equal("Script must have a #MainBlock", pr.ErrorMessage);
    }
}
