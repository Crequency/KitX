using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Abstractions;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Conversion;
using KitX.Workflow.Models.Statements;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Migrated from console-harness TestControlFlow (T29–T38) / TestRoundTrip (T39–T48) /
/// TestComments (T49–T53). 1:1 semantic preservation — each `if(shouldRun("Tn"))` block became
/// a `[Fact]`; `check` → Assert.True, `fail` → Assert.Fail, `pass` → no-op.
/// </summary>
public class RoundTripAndControlFlowTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public RoundTripAndControlFlowTests(WorkflowFixture fx) => _fx = fx;

    // Shared scripts (verbatim multi-line, mirrored from the console harness).
    private const string GuessingGame = @"#ConstBlock
int guessNum = 5;
int targetNum = 7;
int loopMax = 3;

#PubVarBlock
bool cond;

#MainBlock
Print(""开始执行工作流"");
Goto(""ForLoopBlock"");

#Block ForLoopBlock
ForLoop(0, loopMax, 1, ""i"", ""LoopBody"", ""EndLogic"");

#Block LoopBody
i > Print;
guessNum, targetNum > HelperFuncCompare(""BEQ"") > cond;
Branch(cond, ""SuccessLogic"", ""CheckLogic"");

#Block CheckLogic
guessNum, targetNum > HelperFuncCompare(""BLT"") > cond;
Branch(cond, ""LessThanLogic"", ""GreaterThanLogic"");

#Block LessThanLogic
Print(""猜小了"");
Goto(""ForLoopBlock"");

#Block GreaterThanLogic
Print(""猜大了"");
Goto(""ForLoopBlock"");

#Block SuccessLogic
Print(""猜对啦！"");
Goto(""EndLogic"");

#Block EndLogic
Print(""示例工作流结束"");
Exit();";

    private const string WhileDo = @"#ConstBlock
int guessNum = 5;
int targetNum = 7;

#PubVarBlock
int currentLoop;
bool cond;

#MainBlock
0 > currentLoop;
Goto(""LoopCond"");

#Block LoopCond
currentLoop > HelperFuncCompare(""BLE"", _, 100) > cond;
Branch(cond, ""LoopBody"", ""EndLogic"");

#Block LoopBody
currentLoop > Print;
currentLoop > HelperFuncAdd(_, 1) > currentLoop;
Goto(""LoopCond"");

#Block EndLogic
Print(""条件循环结束"");
Exit();";

    // ── TestControlFlow: T29–T38 ──

    [Fact]
    public void T29_BranchJump()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nbool c = true;\n\n#MainBlock\nGoto(\"Cond\");\n\n#Block Cond\ntrue > c;\nBranch(c, \"True\", \"False\");\n\n#Block True\nPrint(\"yes\");\nGoto(\"End\");\n\n#Block False\nPrint(\"no\");\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("yes"), "Branch jump");
    }

    [Fact]
    public void T30_ForLoopCounting()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nint loopMax = 3;\n\n#MainBlock\nGoto(\"ForLoopBlock\");\n\n#Block ForLoopBlock\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\ni > Print;\nGoto(\"ForLoopBlock\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("0") && o.Contains("1") && o.Contains("2") && o.Contains("done"), "ForLoop counting");
    }

    [Fact]
    public void T31_ForLoopReEntryViaGoto()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nint loopMax = 2;\n\n#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\ni > Print;\nGoto(\"LoopHead\");" + TestData.End);
        Assert.True(pr.IsSuccess, "ForLoop re-entry via Goto");
    }

    [Fact]
    public void T32_ForLoopIndexLoopInjected()
    {
        var pr = _fx.Parser.Parse("#ConstBlock\nint loopMax = 2;\n\n#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\ni > Print;\nGoto(\"LoopHead\");" + TestData.End);
        bool ok = pr.IsSuccess && pr.Script != null && (pr.Script.PubVarBlock == null || !pr.Script.PubVarBlock.Variables.Exists(v => v.Name == "i")) && pr.Script.NamedBlocks.GetValueOrDefault("Body")?.BlockVars.All(v => v.Name != "i") == true;
        Assert.True(ok, "ForLoop index loop-injected");
    }

    [Fact]
    public void T33_WhileDoBranchGotoBack()
    {
        var o = _fx.ExecuteScript("#PubVarBlock\nint counter;\nbool cond;\n\n#MainBlock\n0 > counter;\nGoto(\"LoopCond\");\n\n#Block LoopCond\ncounter > HelperFuncCompare(\"BLT\", _, 3) > cond;\nBranch(cond, \"Body\", \"End\");\n\n#Block Body\ncounter > Print;\ncounter > HelperFuncAdd(_, 1) > counter;\nGoto(\"LoopCond\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("0") && o.Contains("1") && o.Contains("2") && o.Contains("done"), "while-do (Branch+Goto back)");
    }

    [Fact]
    public void T34_GotoSequentialJump()
    {
        var o = _fx.ExecuteScript("#MainBlock\nPrint(\"first\");\nGoto(\"Next\");\n\n#Block Next\nPrint(\"second\");\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("first") && o.Contains("second"), "Goto sequential jump");
    }

    [Fact]
    public void T35_ExitTerminatesWorkflow()
    {
        var o = _fx.ExecuteScript("#MainBlock\nPrint(\"before\");\nExit();\n", TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("before"), "Exit terminates workflow");
    }

    [Fact]
    public void T36_SwitchRouting()
    {
        var o = _fx.ExecuteScript("#ConstBlock\nint idx = 1;\n\n#MainBlock\nSwitch(idx, \"Default\", \"ToolA\", \"ToolB\", \"ToolC\");\n\n#Block ToolA\nPrint(\"A\");\nGoto(\"End\");\n\n#Block ToolB\nPrint(\"B\");\nGoto(\"End\");\n\n#Block ToolC\nPrint(\"C\");\nGoto(\"End\");\n\n#Block Default\nPrint(\"default\");\nGoto(\"End\");" + TestData.End, TestData.ExecutionHelpers, 5);
        Assert.True(o != null && o.Contains("B"), "Switch routing (idx=1)");
    }

    [Fact]
    public void T37_DeadCodeWarning()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nPrint(\"executed\");\nGoto(\"End\");\nPrint(\"dead code\");\nGoto(\"End\");" + TestData.End);
        bool hasDeadCode = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_DEAD_CODE");
        if (pr.IsSuccess && hasDeadCode) return;
        Assert.Fail("dead code warning: BS_DEAD_CODE emitted by BS2CG not parser");
    }

    [Fact]
    public void T38_UnterminatedBlockError()
    {
        var pr = _fx.Parser.Parse("#MainBlock\nPrint(\"hi\");\n" + TestData.End);
        bool hasError = pr.Diagnostics != null && pr.Diagnostics.Items.Any(d => d.Code == "BS_UNTERMINATED_BLOCK");
        if (hasError) return;
        Assert.Fail("unterminated block error: BS_UNTERMINATED_BLOCK not emitted");
    }

    // ── TestRoundTrip: T39–T48 ──

    [Fact]
    public void T39_BasicRoundTrip()
    {
        var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + TestData.End;
        var rt = _fx.CfgRoundTrip(src, TestData.DeclHelpers);
        Assert.True(rt != null && WorkflowFixture.TextEquals(src, rt!), "basic round-trip: not text-stable yet");
    }

    [Fact]
    public void T40_StrongTypePubVar()
    {
        var src = "#PubVarBlock\nint currentLoop;\nstring userInput;\n\n#MainBlock\n0 > currentLoop;\nGoto(\"End\");" + TestData.End;
        var rt = _fx.CfgRoundTrip(src, TestData.DeclHelpers);
        bool ok = rt != null && rt.Contains("int currentLoop") && rt.Contains("string userInput");
        Assert.True(ok, "strong type PubVar: lost types");
    }

    [Fact]
    public void T41_NoCapacitorLeak()
    {
        var rt = _fx.CfgRoundTrip(GuessingGame, TestData.DeclHelpers);
        bool ok = rt != null && !rt.Contains("vaaa");
        Assert.True(ok, "no capacitor leak: vaaa leaked");
    }

    [Fact]
    public void T42_ConstBlockPreserved()
    {
        var rt = _fx.CfgRoundTrip(GuessingGame, TestData.DeclHelpers);
        Assert.True(rt != null && rt.Contains("int guessNum = 5;") && rt.Contains("int targetNum = 7;"), "ConstBlock preserved");
    }

    [Fact]
    public void T43_PipelineFormPreserved()
    {
        var src = "#ConstBlock\nstring name = \"World\";\n\n#MainBlock\nname > StringConcat(\"Hi \", _) > Print;\nGoto(\"End\");" + TestData.End;
        var rt = _fx.CfgRoundTrip(src, TestData.DeclHelpers);
        var afterMain = rt?.IndexOf("#MainBlock") is >= 0 and int i ? rt[i..] : "";
        bool ok = rt != null && rt.Contains(">") && !afterMain.Contains(" = ");
        Assert.True(ok, "pipeline form preserved: contains =");
    }

    [Fact]
    public void T44_ControlFlowFormPreserved()
    {
        var rt = _fx.CfgRoundTrip(GuessingGame, TestData.DeclHelpers);
        Assert.True(rt != null && rt.Contains("Branch(") && !rt.Contains("NextBlock = Branch("), "control flow form preserved");
    }

    [Fact]
    public void T45_GotoFormPreserved()
    {
        var rt = _fx.CfgRoundTrip(GuessingGame, TestData.DeclHelpers);
        Assert.True(rt != null && rt.Contains("Goto(") && !rt.Contains("NextBlock = \""), "Goto form preserved");
    }

    [Fact]
    public void T46_GuessingGameRoundTrip()
    {
        var rt = _fx.CfgRoundTrip(GuessingGame, TestData.DeclHelpers);
        Assert.True(rt != null && WorkflowFixture.TextEquals(GuessingGame, rt!), "guessing game round-trip: not text-stable yet");
    }

    [Fact]
    public void T47_WhileDoRoundTrip()
    {
        var rt = _fx.CfgRoundTrip(WhileDo, TestData.DeclHelpers);
        Assert.True(rt != null && WorkflowFixture.TextEquals(WhileDo, rt!), "while-do round-trip: not text-stable yet");
    }

    [Fact]
    public void T48_BlockVarRoundTrip()
    {
        var bvSrc = "#MainBlock\nGoto(\"ProcessBatch\");\n\n#Block ProcessBatch\n##BlockVars\nint processedCount = 0;\nstring currentItem;\n##BlockBody\n\"item1\" > currentItem;\ncurrentItem > Print;\nprocessedCount > HelperFuncAdd(_, 1) > processedCount;\nprocessedCount > Print;\nGoto(\"NextStage\");\n\n#Block NextStage\nPrint(\"done\");\nExit();";
        var rt = _fx.CfgRoundTrip(bvSrc, TestData.DeclHelpers);
        Assert.True(rt != null && WorkflowFixture.TextEquals(bvSrc, rt!), "BlockVar round-trip: not text-stable yet");
    }

    // ── TestComments: T49–T53 ──

    [Fact]
    public void T49_StatementAboveCommentRetained()
    {
        var src = "#ConstBlock\nint a = 3;\n\n#MainBlock\n// This is a statement-level comment\na > Print;\nGoto(\"End\");" + TestData.End;
        var rt = _fx.CfgRoundTrip(src, TestData.DeclHelpers);
        bool ok = rt != null && rt.Contains("// This is a statement-level comment");
        Assert.True(ok, "statement-above comment retained: BSParser drops comments via Ignore");
    }

    [Fact]
    public void T50_InlineCommentRetained()
    {
        var src = "#ConstBlock\nint a = 3;\n\n#MainBlock\na > Print; // inline comment\nGoto(\"End\");" + TestData.End;
        var rt = _fx.CfgRoundTrip(src, TestData.DeclHelpers);
        bool ok = rt != null && rt.Contains("// inline comment");
        Assert.True(ok, "inline comment retained: Inline comment not retained");
    }

    [Fact]
    public void T51_BlockLevelCommentRetained()
    {
        var src = "#ConstBlock\nint a = 3;\n\n#MainBlock\na > Print;\nGoto(\"LoopBody\");\n\n// This is a block-level comment for LoopBody\n#Block LoopBody\na > Print;\nGoto(\"End\");" + TestData.End;
        var rt = _fx.CfgRoundTrip(src, TestData.DeclHelpers);
        bool ok = rt != null && rt.Contains("// This is a block-level comment");
        Assert.True(ok, "block-level comment retained: Block-level comment not retained (§9.3)");
    }

    [Fact]
    public void T52_AllCommentFormsCombined()
    {
        var src = "#ConstBlock\nint a = 3;\n\n// block-level\n#MainBlock\n// statement-above\na > Print;\n// on-goto\nGoto(\"End\");" + TestData.End;
        var rt = _fx.CfgRoundTrip(src, TestData.DeclHelpers);
        bool allThree = rt != null && rt.Contains("// block-level") && rt.Contains("// statement-above") && rt.Contains("// on-goto");
        Assert.True(allThree, "all comment forms combined: Not all forms retained");
    }

    [Fact]
    public void T53_CommentAnchoringCorrectness()
    {
        // Original was a no-op pass (§9.1 anchor correctness) — preserved.
    }
}
