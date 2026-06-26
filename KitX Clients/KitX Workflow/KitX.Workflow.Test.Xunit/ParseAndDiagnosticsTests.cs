using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Models;
using KitX.Workflow.Models.Statements;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Migrated from console-harness TestParsing / TestBlocks / TestPipeline / TestDiagnostics
/// (T00–T28, T54–T60). 1:1 semantic preservation — each `if(shouldRun("Tn"))` block became a
/// `[Fact]`; `check` → Assert.True, `fail` → Assert.Fail, `pass` → no-op (pass by default).
/// </summary>
public class ParseAndDiagnosticsTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public ParseAndDiagnosticsTests(WorkflowFixture fx) => _fx = fx;

    // ── TestInfra: T00, T00b (sanity) ──

    [Fact]
    public void T00_ConsoleHarnessWorks()
    {
        // Original was a no-op pass — preserved.
    }

    [Fact]
    public void T00b_DeclHelpersAvailable()
    {
        Assert.NotEmpty(TestData.DeclHelpers);
        Assert.NotEmpty(TestData.ExecutionHelpers);
    }

    // ── TestParsing: T01–T10 ──

    [Fact]
    public void T01_ParseSimpleExpression()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess, "parse simple");
    }

    [Fact]
    public void T02_ParseLiteralExpressions()
    {
        var pr = _fx.Parser.Parse("#MainBlock\n\"hello\" > Print;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess, "string literal");
    }

    [Fact]
    public void T03_ParseNumericLiterals()
    {
        var pr = _fx.Parser.Parse("#MainBlock\n42 > Print;\n3.14 > Print;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess, "int and double literals");
    }

    [Fact]
    public void T04_ParseBooleanLiterals()
    {
        var pr = _fx.Parser.Parse("#MainBlock\ntrue > Print;\nfalse > Print;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess, "boolean literals");
    }

    [Fact]
    public void T05_ParsePipelineExpression()
    {
        var pr = _fx.Parser.Parse("#MainBlock\n0 > Print;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess, "pipeline expression");
    }

    [Fact]
    public void T06_ParseNestedCall()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nHelperFuncAdd(1, 2) > Print;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess, "nested call");
    }

    [Fact]
    public void T07_ParseMultiBlock()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nGoto(\"A\");\n\n#Block A\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess, "multi-block");
    }

    [Fact]
    public void T08_ParseConstBlock()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nint x = 5;\n\n#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess && pr.Script?.ConstBlock != null && pr.Script.ConstBlock.Variables.Count == 1, "const block");
    }

    [Fact]
    public void T09_ParsePubVarBlock()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\nint x;\n\n#MainBlock\n0 > x;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess && pr.Script?.PubVarBlock != null && pr.Script.PubVarBlock.Variables.Count == 1, "pubvar block");
    }

    [Fact]
    public void T10_ParseBlockVarsAndBody()
    {
        var pr = _fx.Parser.Parse("#Block P\n##BlockVars\nint c = 0;\n##BlockBody\nc > Print;\nGoto(\"End\");\n\n#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess, "blockvars + blockbody");
    }

    // ── TestBlocks: T11–T18 ──

    [Fact]
    public void T11_ConstBlockMustHaveInitValue()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nint x = 5;\n\n#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        var cb = pr.Script?.ConstBlock;
        Assert.True(cb != null && cb.Variables.Count == 1 && (int)cb.Variables[0].DefaultValue! == 5, "ConstBlock must have init value");
    }

    [Fact]
    public void T12_PubVarBlockStrongTypesPreserved()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\nint currentLoop;\nstring userInput = \"\";\n\n#MainBlock\n0 > currentLoop;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        var pb = pr.Script?.PubVarBlock;
        bool ok = pb != null && pb.Variables.Count == 2 && pb.Variables[0].Type == "int" && pb.Variables[1].Type == "string";
        Assert.True(ok, "PubVarBlock strong types preserved: PubVar type is dynamic in round-trip");
    }

    [Fact]
    public void T13_PubVarBlockOptionalInit()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\nint x;\n\n#MainBlock\n0 > x;\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        var pb = pr.Script?.PubVarBlock;
        Assert.True(pb != null && pb.Variables[0].Name == "x" && pb.Variables[0].DefaultValue == null, "PubVarBlock optional init");
    }

    [Fact]
    public void T14_BlockVarsAndBlockBody()
    {
        var pr = _fx.Parser.Parse("#Block ProcessItem\n##BlockVars\nint c = 0;\nstring buf;\n##BlockBody\nc > Print;\nGoto(\"End\");\n\n#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        var blk = pr.Script?.NamedBlocks?.GetValueOrDefault("ProcessItem");
        Assert.True(blk != null && blk.BlockVars.Count == 2 && blk.Statements.Count >= 2, "##BlockVars + ##BlockBody");
    }

    [Fact]
    public void T15_BlockEndOptionalMarker()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nGoto(\"Fin\");\n\n#Block Fin\n##BlockEnd\nPrint(\"final\");\nExit();");
        Assert.True(pr.IsSuccess && pr.Script != null, "##BlockEnd optional marker");
    }

    [Fact]
    public void T16_BlockWithoutBlockVars()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nPrint(\"hi\");\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        var end = pr.Script?.NamedBlocks?.GetValueOrDefault("End");
        Assert.True(end != null && end.Statements.Count >= 2, "#Block without ##BlockVars");
    }

    [Fact]
    public void T17_MissingMainBlock()
    {
        var pr = _fx.Parser.Parse("#Block SomeBlock\nPrint(\"hi\");\nGoto(\"End\");\n\n#Block End\nPrint(\"done\");\nExit();");
        Assert.True(!pr.IsSuccess, "missing #MainBlock");
    }

    [Fact]
    public void T18_MultiBlockComposition()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nint a = 1;\n\n#PubVarBlock\nint x;\n\n#MainBlock\nGoto(\"B1\");\n\n#Block B1\nGoto(\"B2\");\n\n#Block B2\nPrint(\"done\");\nExit();");
        Assert.True(pr.IsSuccess && pr.Script?.AllBlocks.Count == 5, "multi-block composition (4 blocks)");
    }

    // ── TestPipeline: T19–T28 ──

    [Fact]
    public void T19_LinearChainExecution()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("Hi World"), "linear chain execution");
    }

    [Fact]
    public void T20_DiamondDependency()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nstring a = \"Hello\";\nstring b = \"World\";\n\n#MainBlock\na, b > StringConcat > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("HelloWorld"), "diamond dependency");
    }

    [Fact]
    public void T21_PassThroughTap()
    {
        var o = _fx.ExecuteScript("#PubVarBlock\nint x;\n\n#MainBlock\n0 > x > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("0"), "pass-through (tap)");
    }

    [Fact]
    public void T22_SelfIncrement()
    {
        var o = _fx.ExecuteScript("#PubVarBlock\nint x;\n\n#MainBlock\n5 > x;\nx > HelperFuncAdd(_, 1) > x;\nx > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("6"), "self-increment");
    }

    [Fact]
    public void T23_MixedParamsPlaceholder()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nint x = 10;\n\n#PubVarBlock\nint result;\n\n#MainBlock\nx > HelperFuncAdd(_, 1) > result;\nresult > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("11"), "mixed params (placeholder)");
    }

    [Fact]
    public void T24_MixedParamsNoPlaceholder()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nint a = 5;\nint b = 5;\n\n#PubVarBlock\nbool cond;\n\n#MainBlock\na, b > HelperFuncCompare(\"BEQ\") > cond;\ncond > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("True"), "mixed params (no placeholder)");
    }

    [Fact]
    public void T25_PurePipelineAssignment()
    {
        var o = _fx.ExecuteScript("#PubVarBlock\nint currentLoop;\n\n#MainBlock\n0 > currentLoop;\ncurrentLoop > Print;\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("0"), "pure pipeline assignment");
    }

    [Fact]
    public void T26_MultiLinePipeline()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nstring data = \"test\";\n\n#MainBlock\ndata\n    > StringConcat(\"p: \", _)\n    > Print;\nGoto(\"End\");" + TestData.End);
        Assert.True(pr.IsSuccess, "multi-line pipeline");
    }

    [Fact]
    public void T27_ControlFlowNotAsPipelineTarget()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\nbool cond;\n\n#MainBlock\ntrue > cond;\ncond > Branch(_, \"A\", \"B\");\nGoto(\"End\");" + TestData.End);
        Assert.True(pr.IsSuccess, "control flow not as pipeline target");
    }

    [Fact]
    public void T28_ControlFlowBareCall()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\nbool cond;\n\n#MainBlock\ntrue > cond;\nBranch(cond, \"A\", \"B\");\n\n#Block A\nPrint(\"true\");\nGoto(\"End\");\n\n#Block B\nPrint(\"false\");\nGoto(\"End\");" + TestData.End);
        bool ok = pr.IsSuccess && pr.Script!.MainBlock.Statements.Count >= 2 && pr.Script.MainBlock.Statements[1] is FlowControlStatement fcs && fcs.FunctionName == "Branch";
        Assert.True(ok, "control flow bare call");
    }

    // ── TestDiagnostics: T54–T60 ──

    [Fact]
    public void T54_NestedCallError()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nPrint(Get(\"x\"));\nGoto(\"End\");" + TestData.End);
        var diag = pr.Diagnostics;
        Assert.True(diag != null && diag.Items.Any(d => d.Code == "BS_NESTED_CALL"), "nested call → BS_NESTED_CALL error");
    }

    [Fact]
    public void T55_GlobalAssignmentError()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\nint x;\n\n#MainBlock\nx = 42;\nGoto(\"End\");" + TestData.End);
        bool ok = !pr.IsSuccess || (pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_ILLEGAL_ASSIGNMENT"));
        Assert.True(ok, "global = assignment → error: Parser accepts = without error");
    }

    [Fact]
    public void T56_UnterminatedBlockError()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nPrint(\"hi\");\n");
        bool hasErr = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_UNTERMINATED_BLOCK");
        Assert.True(hasErr, "unterminated block → error: BS_UNTERMINATED_BLOCK not emitted (§7.7)");
    }

    [Fact]
    public void T57_ConstBlockWriteError()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nint x = 5;\n\n#MainBlock\n0 > x;\nGoto(\"End\");" + TestData.End);
        var diag = pr.Diagnostics;
        Assert.True(diag != null && diag.HasErrors, "ConstBlock write → error: ConstBlock read-only enforcement not implemented (§3.1)");
    }

    [Fact]
    public void T58_ForLoopIndexWriteError()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nint loopMax = 3;\n\n#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\n5 > i;\nGoto(\"LoopHead\");" + TestData.End);
        var diag = pr.Diagnostics;
        Assert.True(diag != null && diag.HasErrors, "ForLoop index write → error: ForLoop index read-only enforcement not implemented (§7.1)");
    }

    [Fact]
    public void T59_PlaceholderCannotBeVariableName()
    {
        var pr = _fx.Parser.Parse("#PubVarBlock\nint _;\n\n#MainBlock\n0 > _;\nGoto(\"End\");" + TestData.End);
        bool hasErr = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_RESERVED_PLACEHOLDER");
        Assert.True(hasErr, "placeholder _ cannot be variable name: BS_RESERVED_PLACEHOLDER not emitted");
    }

    [Fact]
    public void T60_DeadCodeWarning()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nPrint(\"executed\");\nGoto(\"End\");\nPrint(\"dead code\");" + TestData.End);
        bool hasDeadCode = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_DEAD_CODE");
        Assert.True(pr.IsSuccess && hasDeadCode, "dead code warning: BS_DEAD_CODE emitted by BS2CG not parser");
    }
}
