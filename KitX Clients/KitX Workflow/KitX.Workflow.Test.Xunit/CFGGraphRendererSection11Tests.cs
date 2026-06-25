using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Conversion;
using Xunit;
using BP = KitX.Core.Contract.Workflow.Blueprint;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// RED-LIGHT suite for §11 Blueprint rendering (stable subset). These assert the rules
/// the minimal CFGGraphRenderer does NOT yet implement:
///   §11.3 — inter-block Exec connections for Goto / Branch / ForLoop / Switch, sourced
///           from the BlockNode's Exec output pins (control-flow functions emit NO standalone node).
///   §11.1 — Block node fields (title = block name, Exec pins, Comment from block comment).
///
/// Out of scope (see Blueprint-Editor-Redesign-Plan.md / discussion):
///   §11.2 — generic EntryPoint/ExitPoint data-boundary derivation (meaningless for BS, whose
///           cross-block data flow is PubVar-only via global storage) and ForLoop index EntryPoint
///           (pending ForLoop → Each evolution); BlockVar inner VariableNode (BlockVar to be removed).
///   §11.4 — auto-temp long-name (§14.2 undecided); position preservation (deferred).
///
/// Until the renderer is extended, every test below fails on a missing Exec connection / pin.
/// </summary>
public class CFGGraphRendererSection11Tests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _fx;
    public CFGGraphRendererSection11Tests(WorkflowFixture fx) => _fx = fx;

    private BP Render(string src) =>
        _fx.GetService<ICFGGraphRenderer>().Render(_fx.BS2CFG(src, TestData.DeclHelpers)!);

    /// <summary>Helper: find a BlockNode by its block name (null if MainBlock, which renders as Entry).</summary>
    private static BlockNode? BlockByName(BP bp, string name) =>
        bp.Nodes.OfType<BlockNode>().FirstOrDefault(b => b.BlockName == name);

    // ── §11.3 Exec connections ────────────────────────────────────────────────

    /// <summary>§11.3: <c>Goto("End")</c> → current block's single Exec output pin ("Exec") wired
    /// to End block's Exec input pin.</summary>
    [Fact]
    public void Render_Goto_ProducesSingleExecEdge()
    {
        var bp = Render("""
#PubVarBlock
int x;
#MainBlock
0 > x;
Goto("End");
""" + TestData.End);

        var endNode = BlockByName(bp, "End");
        Assert.NotNull(endNode);

        // Exactly one Exec connection whose source pin is named "Exec" and whose target is the End block.
        var gotoEdges = bp.Connections.Where(c =>
            c.TargetNodeId == endNode!.Id &&
            bp.GetNodeById(c.SourceNodeId) is not null).ToList();
        var execEdge = gotoEdges.FirstOrDefault(c =>
            PinName(bp, c.SourceNodeId, c.SourcePinId) == "Exec");
        Assert.True(execEdge is not null,
            $"Goto should produce one Exec edge to End. Saw {gotoEdges.Count} edges into End.");
    }

    /// <summary>§11.3: <c>Branch(cond,"T","F")</c> → two Exec output pins (True/False) wired to T and F.</summary>
    [Fact]
    public void Render_Branch_ProducesTwoExecEdges_TrueFalse()
    {
        var bp = Render("""
#PubVarBlock
bool cond;
#MainBlock
0 > cond;
Branch(cond, "TrueBlock", "FalseBlock");
#Block TrueBlock
Print("yes");
Goto("End");
#Block FalseBlock
Print("no");
Goto("End");
""" + TestData.End);

        var tNode = BlockByName(bp, "TrueBlock");
        var fNode = BlockByName(bp, "FalseBlock");
        Assert.NotNull(tNode);
        Assert.NotNull(fNode);

        Assert.True(HasExecEdge(bp, "True", tNode!.Id),
            "Branch should emit a True Exec edge to TrueBlock");
        Assert.True(HasExecEdge(bp, "False", fNode!.Id),
            "Branch should emit a False Exec edge to FalseBlock");
    }

    /// <summary>§11.3: <c>ForLoop(...,"body","end")</c> → LoopBody/LoopEnd Exec edges.</summary>
    [Fact]
    public void Render_ForLoop_ProducesLoopBodyAndLoopEndEdges()
    {
        var bp = Render("""
#MainBlock
ForLoop(0, 3, 1, "i", "LoopBody", "EndLogic");
#Block LoopBody
i > Print;
Goto("MainBlock");
#Block EndLogic
Print("done");
Goto("End");
""" + TestData.End);

        var bodyNode = BlockByName(bp, "LoopBody");
        var endLogicNode = BlockByName(bp, "EndLogic");
        Assert.NotNull(bodyNode);
        Assert.NotNull(endLogicNode);

        Assert.True(HasExecEdge(bp, "LoopBody", bodyNode!.Id),
            "ForLoop should emit a LoopBody Exec edge to the body block");
        Assert.True(HasExecEdge(bp, "LoopEnd", endLogicNode!.Id),
            "ForLoop should emit a LoopEnd Exec edge to the end block");
    }

    /// <summary>§11.3 (general rule): <c>Switch(selector,"default","b0","b1")</c> → per-case Exec edges.</summary>
    [Fact]
    public void Render_Switch_ProducesPerCaseExecEdges()
    {
        var bp = Render("""
#PubVarBlock
int sel;
#MainBlock
1 > sel;
Switch(sel, "DefaultBlock", "ZeroBlock", "OneBlock");
#Block ZeroBlock
Print("0");
Goto("End");
#Block OneBlock
Print("1");
Goto("End");
#Block DefaultBlock
Print("d");
Goto("End");
""" + TestData.End);

        Assert.True(HasExecEdge(bp, "Default", BlockByName(bp, "DefaultBlock")!.Id),
            "Switch should emit a Default Exec edge");
        Assert.True(HasExecEdge(bp, "0", BlockByName(bp, "ZeroBlock")!.Id),
            "Switch should emit a case-0 Exec edge");
        Assert.True(HasExecEdge(bp, "1", BlockByName(bp, "OneBlock")!.Id),
            "Switch should emit a case-1 Exec edge");
    }

    /// <summary>§11.3: every Exec connection's source AND target pin must be PinType.Execution.</summary>
    [Fact]
    public void Render_ExecConnections_HaveExecutionPinType()
    {
        var bp = Render("""
#PubVarBlock
int x;
#MainBlock
0 > x;
Goto("Worker");
#Block Worker
x > Print;
Goto("End");
""" + TestData.End);

        // An Exec connection is one whose source pin is an Execution pin (the BlockNode's out pins).
        var execEdges = bp.Connections.Where(c =>
            PinTypeOf(bp, c.SourceNodeId, c.SourcePinId) == PinType.Execution).ToList();
        Assert.NotEmpty(execEdges);
        Assert.All(execEdges, c =>
        {
            Assert.Equal(PinType.Execution, PinTypeOf(bp, c.TargetNodeId, c.TargetPinId));
        });
    }

    // ── §11.1 Block node fields ───────────────────────────────────────────────

    /// <summary>§11.1: the BlockNode's title is the block name (not the #Block token).</summary>
    [Fact]
    public void Render_BlockNode_TitleIsBlockName()
    {
        var bp = Render("""
#MainBlock
Goto("Worker");
#Block Worker
Print("hi");
Goto("End");
""" + TestData.End);

        var worker = BlockByName(bp, "Worker");
        Assert.NotNull(worker);
        Assert.Equal("Worker", worker!.BlockName);
        Assert.Equal("Worker", worker.Name);
    }

    /// <summary>§11.1: a BlockNode carries a standard Exec input pin and at least one Exec output pin.</summary>
    [Fact]
    public void Render_BlockNode_HasExecPins()
    {
        var bp = Render("""
#MainBlock
Goto("Worker");
#Block Worker
Print("hi");
Goto("End");
""" + TestData.End);

        var worker = BlockByName(bp, "Worker");
        Assert.NotNull(worker);
        Assert.Contains(worker!.InputPins, p => p.Type == PinType.Execution);
        Assert.Contains(worker.OutputPins, p => p.Type == PinType.Execution);
    }

    /// <summary>§11.1 (ref §9.3): a block-level BS comment populates the BlockNode.Comment field.</summary>
    [Fact]
    public void Render_BlockNode_CommentFromBlockComment()
    {
        var bp = Render("""
#MainBlock
// this is the entry
Goto("Worker");
#Block Worker
// processes the payload
Print("hi");
Goto("End");
""" + TestData.End);

        // The MainBlock carries a block comment → the Entry/Block node should reflect it.
        var mainNode = bp.Nodes.FirstOrDefault(n => n.NodeType != BlueprintNodeType.Variable);
        Assert.NotNull(mainNode);
        Assert.False(string.IsNullOrEmpty(mainNode!.Comment),
            "Block-level comment should populate the node's Comment field");
    }

    /// <summary>§11.1 + §11.3: a BlockNode's Exec output pin count matches its terminator's arm count
    /// (Goto → 1, Branch → 2, ForLoop → 2).</summary>
    [Fact]
    public void Render_BlockNode_ExecOutputPinCount_MatchesTerminatorArms()
    {
        var bp = Render("""
#PubVarBlock
bool cond;
#MainBlock
0 > cond;
Branch(cond, "TrueBlock", "FalseBlock");
#Block TrueBlock
Goto("End");
#Block FalseBlock
Goto("End");
""" + TestData.End);

        // MainBlock ends in Branch → expect 2 Exec output pins (True, False).
        var mainBlock = bp.Nodes.FirstOrDefault(n => n.NodeType == BlueprintNodeType.Entry);
        Assert.NotNull(mainBlock);
        var execOutPins = mainBlock!.OutputPins.Where(p => p.Type == PinType.Execution).ToList();
        Assert.True(execOutPins.Count >= 2,
            $"Branch terminator should yield ≥2 Exec output pins; saw {execOutPins.Count}");
    }

    // ── §11.3 boundary (no standalone flow nodes) ─────────────────────────────

    /// <summary>§11.3: Branch/ForLoop/Switch/Goto emit NO standalone BuiltinFunction node in the outer
    /// graph — they exist only as the semantic source of inter-block Exec wires.</summary>
    [Fact]
    public void Render_ControlFlow_DoesNotProduceStandaloneFlowNode()
    {
        var bp = Render("""
#PubVarBlock
bool cond;
#MainBlock
0 > cond;
Branch(cond, "TrueBlock", "FalseBlock");
#Block TrueBlock
Goto("End");
#Block FalseBlock
Goto("End");
""" + TestData.End);

        Assert.DoesNotContain(bp.Nodes, n =>
            n.NodeType == BlueprintNodeType.BuiltinFunction &&
            n is BuiltinFunctionNode bf &&
            (bf.FunctionName == "Branch" || bf.FunctionName == "ForLoop" ||
             bf.FunctionName == "Switch" || bf.FunctionName == "Goto"));
    }

    // ── pin lookup helpers ────────────────────────────────────────────────────

    private static string? PinName(BP bp, string nodeId, string pinId)
    {
        var node = bp.GetNodeById(nodeId);
        return node?.GetPinById(pinId)?.Name;
    }

    private static PinType? PinTypeOf(BP bp, string nodeId, string pinId)
    {
        var node = bp.GetNodeById(nodeId);
        return node?.GetPinById(pinId)?.Type;
    }

    /// <summary>True iff an Exec connection exists whose source pin name is <paramref name="pinName"/>
    /// and whose target node id is <paramref name="targetNodeId"/>.</summary>
    private static bool HasExecEdge(BP bp, string pinName, string targetNodeId) =>
        bp.Connections.Any(c =>
            c.TargetNodeId == targetNodeId &&
            PinName(bp, c.SourceNodeId, c.SourcePinId) == pinName &&
            PinTypeOf(bp, c.SourceNodeId, c.SourcePinId) == PinType.Execution);
}
