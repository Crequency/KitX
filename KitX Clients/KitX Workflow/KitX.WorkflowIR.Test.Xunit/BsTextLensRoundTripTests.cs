using KitX.Workflow.Builtin;
using KitX.Workflow.Ir;
using KitX.Workflow.Ir.Lowering;
using KitX.Workflow.Lens.BsTextLens;
using Xunit;

namespace KitX.Workflow.Test.Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// BsTextLensRoundTripTests — BS text ⇄ IR round-trip equivalence.
//
// Adapted from the legacy StableIdRoundTripTests + RoundTripAndControlFlowTests,
// retargeted at the new IR + Lens stack. The core invariant under test: a BS script
// lowered to IrWorkflow and rendered back to text must, when re-parsed, yield an
// IrWorkflow that is STRUCTURALLY EQUAL to the first (block membership, statements,
// fingerprints, control-flow targets, declarations). This is the precondition for
// any diff-based sync and for the "IR is the single source of truth" architecture.
//
// Because the renderer normalises whitespace and capacitor names (which the new IR
// never introduces), equality is checked at the IR level (Lower(text).Ir) rather
// than as raw text. The fingerprint stability tests additionally lock the property
// that the SAME text always lowers to the SAME fingerprints.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Verifies the BS text lens round-trips: BS → IR → BS → IR yields equal IRs, and
/// stable fingerprints hold across re-parse. Mirrors the legacy T39–T53 + the
/// StableId suite's structural-equivalence tests.
/// </summary>
public class BsTextLensRoundTripTests
{
    private static BsTextLens Lens()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        return new BsTextLens(registry);
    }

    private const string End = "\n\n#Block End\nPrint(\"done\");\nExit();";

    // ── Pipeline + expression round-trip (T39/T43 analogues) ──

    [Fact]
    public void RoundTrip_SimplePrint_YieldsEqualIr()
    {
        var src = "#MainBlock\nPrint(\"hello\");\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_PipelineMultiSegment_PreservesStructure()
    {
        // a, b > StringConcat > Print  — two sources, two targets.
        var src = "#ConstBlock\nstring a = \"A\";\nstring b = \"B\";\n\n#MainBlock\na, b > StringConcat > Print;\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_PipelineWithPlaceholderAndTap_PreservesStructure()
    {
        // currentLoop > HelperFuncAdd(_, 1) > currentLoop — placeholder arg + variable tap.
        var src = "#PubVarBlock\nint currentLoop;\n\n#MainBlock\n0 > currentLoop;\ncurrentLoop > HelperFuncAdd(_, 1) > currentLoop;\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_PassThroughTap_BareIdentifierTarget()
    {
        // 0 > x > Print — the bare `x` target is a variable tap (no parens).
        var src = "#PubVarBlock\nint x;\n\n#MainBlock\n0 > x > Print;\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        // Confirm the tap lowered as a Variable segment, not a zero-arg call.
        var pipe = (IrPipelineStatement)ir1.EntryBlock.Statements[0];
        Assert.Equal(IrSegmentKind.Variable, pipe.Segments[0].Kind);
        Assert.Equal("x", pipe.Segments[0].VariableName);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    // ── Control-flow round-trip (T44/T45 analogues) ──

    [Fact]
    public void RoundTrip_Branch_PreservesTargets()
    {
        var src = "#PubVarBlock\nbool cond;\n\n#MainBlock\ntrue > cond;\nBranch(cond, \"True\", \"False\");\n\n#Block True\nPrint(\"yes\");\nGoto(\"End\");\n\n#Block False\nPrint(\"no\");\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
        // And the Branch targets specifically survived.
        var cf = ir2.EntryBlock.Statements.OfType<IrControlFlowStatement>()
            .Single(s => s.Op == ControlFlowOp.Branch);
        Assert.Equal("True", cf.Targets[0].TargetBlockName);
        Assert.Equal("False", cf.Targets[1].TargetBlockName);
    }

    [Fact]
    public void RoundTrip_ForLoop_PreservesArguments()
    {
        var src = "#ConstBlock\nint loopMax = 3;\n\n#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, loopMax, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\ni > Print;\nGoto(\"LoopHead\");" + End;
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
        var cf = ir2.GetBlock("LoopHead")!.Statements.OfType<IrControlFlowStatement>()
            .Single(s => s.Op == ControlFlowOp.ForLoop);
        Assert.Equal(4, cf.Arguments.Length);
        Assert.Equal("0", cf.Arguments[0]);
        Assert.Equal("i", cf.Arguments[3]);
    }

    [Fact]
    public void RoundTrip_Switch_PreservesArms()
    {
        var src = "#MainBlock\nSwitch(0, \"Def\", \"A\", \"B\");\n\n#Block Def\nExit();\n\n#Block A\nExit();\n\n#Block B\nExit();";
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_GotoAndExit_PreservesForm()
    {
        var src = "#MainBlock\nPrint(\"first\");\nGoto(\"Next\");\n\n#Block Next\nPrint(\"second\");\nExit();";
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Break_RendersEmptyTargets()
    {
        var src = "#MainBlock\nGoto(\"LoopHead\");\n\n#Block LoopHead\nForLoop(0, 2, 1, \"i\", \"Body\", \"End\");\n\n#Block Body\ni > Print;\nGoto(\"LoopHead\");\n\n#Block EarlyOut\nBreak();\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_Flip_TwoWayAlternating_PreservesArms()
    {
        var src = "#MainBlock\nFlip(\"A\", \"B\");\n\n#Block A\nPrint(\"odd\");\nGoto(\"End\");\n\n#Block B\nPrint(\"even\");\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        var cf = ir1.EntryBlock.Statements.OfType<IrControlFlowStatement>().Single();
        Assert.Equal("A", cf.Targets[0].TargetBlockName);
        Assert.Equal("B", cf.Targets[1].TargetBlockName);
        var rendered = Lens().Project(ir1);
        Assert.Contains("Flip(\"A\", \"B\")", rendered);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    // ── Full multi-block workflow (the GuessingGame + WhileDo from the legacy suite) ──

    private const string WhileDo = """
        #ConstBlock
        int guessNum = 5;
        int targetNum = 7;

        #PubVarBlock
        int currentLoop;
        bool cond;

        #MainBlock
        0 > currentLoop;
        Goto("LoopCond");

        #Block LoopCond
        currentLoop > HelperFuncCompare("BLE", _, 100) > cond;
        Branch(cond, "LoopBody", "EndLogic");

        #Block LoopBody
        currentLoop > Print;
        currentLoop > HelperFuncAdd(_, 1) > currentLoop;
        Goto("LoopCond");

        #Block EndLogic
        Print("条件循环结束");
        Exit();
        """;

    [Fact]
    public void RoundTrip_WhileDoWorkflow_PreservesStructure()
    {
        var ir1 = Lens().Parse(WhileDo);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    private const string GuessingGame = """
        #ConstBlock
        int guessNum = 5;
        int targetNum = 7;
        int loopMax = 3;

        #PubVarBlock
        bool cond;

        #MainBlock
        Goto("ForLoopBlock");

        #Block ForLoopBlock
        ForLoop(0, loopMax, 1, "i", "LoopBody", "EndLogic");

        #Block LoopBody
        guessNum, targetNum > HelperFuncCompare("BEQ") > cond;
        Branch(cond, "SuccessLogic", "CheckLogic");

        #Block CheckLogic
        guessNum, targetNum > HelperFuncCompare("BLT") > cond;
        Branch(cond, "LessThanLogic", "GreaterThanLogic");

        #Block LessThanLogic
        Print("猜小了");
        Goto("ForLoopBlock");

        #Block GreaterThanLogic
        Print("猜大了");
        Goto("ForLoopBlock");

        #Block SuccessLogic
        Print("猜对啦！");
        Goto("EndLogic");

        #Block EndLogic
        Print("示例工作流结束");
        Exit();
        """;

    [Fact]
    public void RoundTrip_GuessingGameWorkflow_PreservesStructure()
    {
        var ir1 = Lens().Parse(GuessingGame);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    // ── Declaration blocks ──

    [Fact]
    public void RoundTrip_ConstBlock_PreservesDeclarations()
    {
        var src = "#ConstBlock\nint a = 42;\nstring name = \"World\";\n\n#MainBlock\nname > Print;\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        Assert.Equal(2, ir1.Constants.Count);
        Assert.Equal(42, ir1.Constants["a"].DefaultValue);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_PubVarBlock_PreservesStrongTypes()
    {
        var src = "#PubVarBlock\nint currentLoop;\nstring userInput;\n\n#MainBlock\n0 > currentLoop;\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        Assert.Equal("int", ir1.GlobalVars["currentLoop"].Type);
        Assert.Equal("string", ir1.GlobalVars["userInput"].Type);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    [Fact]
    public void RoundTrip_BlockVars_PreservesBody()
    {
        var src = "#MainBlock\nGoto(\"ProcessBatch\");\n\n#Block ProcessBatch\n##BlockVars\nint processedCount = 0;\nstring currentItem;\n##BlockBody\n\"item1\" > currentItem;\ncurrentItem > Print;\nGoto(\"NextStage\");\n\n#Block NextStage\nPrint(\"done\");\nExit();";
        var ir1 = Lens().Parse(src);
        var batch = ir1.GetBlock("ProcessBatch")!;
        Assert.Equal(2, batch.BlockVars.Length);
        Assert.True(batch.HasExplicitBlockBody);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(ir1, ir2);
    }

    // ── Comment retention (T49–T52 analogues) ──

    [Fact]
    public void RoundTrip_StatementComment_Preserved()
    {
        var src = "#ConstBlock\nint a = 3;\n\n#MainBlock\n// greet\na > Print;\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        var first = ir1.EntryBlock.Statements[0];
        Assert.Equal("greet", first.Comment);
        var rendered = Lens().Project(ir1);
        Assert.Contains("// greet", rendered);
    }

    [Fact]
    public void RoundTrip_BlockLevelComment_PreservedInAnnotation()
    {
        var src = "#ConstBlock\nint a = 3;\n\n#MainBlock\na > Print;\nGoto(\"LoopBody\");\n\n// a labelled block\n#Block LoopBody\na > Print;\nGoto(\"End\");" + End;
        var ir1 = Lens().Parse(src);
        var rendered = Lens().Project(ir1);
        Assert.Contains("// a labelled block", rendered);
    }

    // ── Fingerprint stability (StableId suite analogues) ──

    [Fact]
    public void Fingerprint_StableAcrossReparse()
    {
        var ir1 = Lens().Parse(WhileDo);
        var ir2 = Lens().Parse(WhileDo);
        // Same text twice → identical fingerprints per block (the diff precondition).
        Assert.Equal(
            FingerprintsOf(ir1),
            FingerprintsOf(ir2));
    }

    [Fact]
    public void Fingerprint_StableAcrossRoundTrip()
    {
        var ir1 = Lens().Parse(WhileDo);
        var rendered = Lens().Project(ir1);
        var ir2 = Lens().Parse(rendered);
        Assert.Equal(
            FingerprintsOf(ir1),
            FingerprintsOf(ir2));
    }

    // ── No capacitor leak (T41 analogue): the new IR never synthesises capacitors ──

    [Fact]
    public void Render_NeverEmitsCapacitorNames()
    {
        var ir = Lens().Parse(GuessingGame);
        var rendered = Lens().Project(ir);
        Assert.DoesNotContain("vaaa", rendered);
        Assert.DoesNotContain("vbbb", rendered);
    }

    // ── Block-name identity (block names are the diff anchor) ──

    [Fact]
    public void RoundTrip_PreservesBlockNames()
    {
        var ir = Lens().Parse(WhileDo);
        var names = ir.Blocks.Select(b => b.Name).ToList();
        Assert.Contains("MainBlock", names);
        Assert.Contains("LoopCond", names);
        Assert.Contains("LoopBody", names);
        Assert.Contains("EndLogic", names);
    }

    /// <summary>
    /// Serialises each block to (name, fingerprint-list) as a string, so two IRs can be
    /// compared by structural identity (the diff anchor) without reference-equality on
    /// the nested List&lt;string&gt; tripping up collection comparison.
    /// </summary>
    private static List<string> FingerprintsOf(IrWorkflow ir)
        => ir.Blocks.Select(b => $"{b.Name}: [{string.Join(" | ", b.Statements.Select(s => s.Fingerprint.Value))}]").ToList();
}
