using System.Collections.Immutable;
using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Builtin;
using KitX.Workflow.Diff;
using KitX.Workflow.Ir;
using KitX.Workflow.Lens.BpGraphLens;
using Xunit;
using BP = KitX.Core.Contract.Workflow.Blueprint;

namespace KitX.Workflow.Test.Xunit;

/// <summary>
/// Verifies the BpGraphLens: the IR → Blueprint renderer (§11) and the BP-edit →
/// IrDiff translator. This is the greenfield successor to the legacy
/// CFGGraphRendererTests / CFGGraphRendererSection11Tests / BpEditApplierTests.
///
/// Coverage:
///   §11.1 — each IrBlock → BlockNode (entry → EntryNode); title/comment/Exec pin.
///   §11.2 — ForLoop index EntryPointNode + BlockVar inner VariableNode.
///   §11.3 — control-flow terminator arms → Exec output pins + Exec wires
///           (Branch True/False, ForLoop LoopBody/LoopEnd, Goto Exec, Switch N).
///   §11.4 — data edges from PubVar-writing statements correctly sourced to the
///           producing statement node (the fix for the legacy empty-SourceNodeId hack).
///   layout — IrAnnotation(Layout) → BlueprintNode X/Y (block anchor + per-statement).
///   BpEditTranslator — AddNode/DeleteNode/SetNodeArgument/SetControlFlowArm/
///           MoveNodePosition/AddBlock/DeleteBlock → IrDiff correctness.
///   hardcoded-dict guard — BpEditTranslator has no DashboardToCfgName field;
///           BP-name → IR-name goes through registry.GetBpReverseByBpName.
/// </summary>
public class BpGraphLensTests
{
    // Discover the full builtin registry once (the IR assembly holds all 32 funcs).
    private static BuiltinFunctionRegistry BuildRegistry()
        => BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);

    private static BpGraphLens NewLens() => new(BuildRegistry());

    // ── §11.1 ──────────────────────────────────────────────────────────────

    /// <summary>§11.1: a synthetic EntryNode is always present; every IrBlock
    /// (including the entry block) renders as a BlockNode. The EntryNode's Exec
    /// output connects to the entry block's BlockNode.</summary>
    [Fact]
    public void Render_EntryNode_IsSynthetic_EntryBlock_IsBlockNode()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("Worker")),
            MakeBasic("Worker", MakeExit()));

        var bp = lens.Project(ir);

        // Exactly one EntryNode (the synthetic entry marker).
        var entry = Assert.Single(bp.Nodes.Where(n => n.NodeType == BlueprintNodeType.Entry));
        Assert.Equal("entry:__synthetic__", entry.Id);

        // Both the entry block and Worker are BlockNodes.
        var mainBlock = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "#MainBlock");
        Assert.True(mainBlock.IsMainBlock);
        var worker = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "Worker");
        Assert.Equal("Worker", worker.Name);

        // EntryNode → entry block BlockNode Exec connection exists.
        Assert.Contains(bp.Connections, c =>
            c.SourceNodeId == entry.Id && c.TargetNodeId == mainBlock.Id);
    }

    /// <summary>§11.1: a block-level IrAnnotation(Comment) populates the BlockNode.Comment
    /// (on the entry block's BlockNode, not the synthetic EntryNode).</summary>
    [Fact]
    public void Render_BlockComment_PopulatesNodeComment()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(MakeEntry(
            statements: [MakeGoto("End")],
            annotations: [new IrAnnotation(AnnotationKind.Comment, "Block", "the main block")]));

        var bp = lens.Project(ir);

        var mainBlock = bp.Nodes.OfType<BlockNode>().Single(b => b.IsMainBlock);
        Assert.Equal("the main block", mainBlock.Comment);
    }

    /// <summary>§11.1: a BlockNode carries a standard Exec input pin.</summary>
    [Fact]
    public void Render_BlockNode_HasExecInputPin()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("Worker")),
            MakeBasic("Worker", MakeExit()));

        var bp = lens.Project(ir);

        var worker = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "Worker");
        Assert.Contains(worker.InputPins, p => p.Type == PinType.Execution);
    }

    // ── §11.3 Exec edges ───────────────────────────────────────────────────

    /// <summary>§11.3: Goto(target) → one Exec output pin ("Exec") wired to the target block.</summary>
    [Fact]
    public void Render_Goto_ProducesSingleExecEdge()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("End")),
            MakeBasic("End", MakeExit()));

        var bp = lens.Project(ir);

        var endNode = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "End");
        Assert.True(HasExecEdge(bp, "Exec", endNode.Id),
            "Goto should produce one Exec edge to End.");
    }

    /// <summary>§11.3: Branch(cond,"T","F") → two Exec output pins (True/False) wired to T and F.</summary>
    [Fact]
    public void Render_Branch_ProducesTwoExecEdges_TrueFalse()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeBranch("cond", "TrueBlock", "FalseBlock")),
            MakeBasic("TrueBlock", MakeExit()),
            MakeBasic("FalseBlock", MakeExit()));

        var bp = lens.Project(ir);

        var tNode = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "TrueBlock");
        var fNode = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "FalseBlock");
        Assert.True(HasExecEdge(bp, "True", tNode.Id), "Branch should emit a True Exec edge.");
        Assert.True(HasExecEdge(bp, "False", fNode.Id), "Branch should emit a False Exec edge.");
    }

    /// <summary>§11.3: ForLoop(...,"body","end") → LoopBody/LoopEnd Exec edges.</summary>
    [Fact]
    public void Render_ForLoop_ProducesLoopBodyAndLoopEndEdges()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeForLoop("0", "3", "1", "i", "LoopBody", "EndLogic")),
            MakeBasic("LoopBody", MakeGoto("End")),
            MakeBasic("EndLogic", MakeExit()));

        var bp = lens.Project(ir);

        var bodyNode = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "LoopBody");
        var endNode = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "EndLogic");
        Assert.True(HasExecEdge(bp, "LoopBody", bodyNode.Id), "ForLoop should emit a LoopBody edge.");
        Assert.True(HasExecEdge(bp, "LoopEnd", endNode.Id), "ForLoop should emit a LoopEnd edge.");
    }

    /// <summary>§11.3: Switch(selector,"default","b0","b1") → per-case Exec edges.</summary>
    [Fact]
    public void Render_Switch_ProducesPerCaseExecEdges()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeSwitch("sel", "DefaultBlock", "ZeroBlock", "OneBlock")),
            MakeBasic("DefaultBlock", MakeExit()),
            MakeBasic("ZeroBlock", MakeExit()),
            MakeBasic("OneBlock", MakeExit()));

        var bp = lens.Project(ir);

        Assert.True(HasExecEdge(bp, "Default", BlockId(bp, "DefaultBlock")), "Switch should emit Default edge.");
        Assert.True(HasExecEdge(bp, "0", BlockId(bp, "ZeroBlock")), "Switch should emit case-0 edge.");
        Assert.True(HasExecEdge(bp, "1", BlockId(bp, "OneBlock")), "Switch should emit case-1 edge.");
    }

    /// <summary>§11.3: every Exec connection's source AND target pin must be PinType.Execution.</summary>
    [Fact]
    public void Render_ExecConnections_HaveExecutionPinType()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("Worker")),
            MakeBasic("Worker", MakeExit()));

        var bp = lens.Project(ir);

        var execEdges = bp.Connections.Where(c =>
            PinTypeOf(bp, c.SourceNodeId, c.SourcePinId) == PinType.Execution).ToList();
        Assert.NotEmpty(execEdges);
        Assert.All(execEdges, c =>
            Assert.Equal(PinType.Execution, PinTypeOf(bp, c.TargetNodeId, c.TargetPinId)));
    }

    /// <summary>§11.3: control-flow terminators emit NO standalone BuiltinFunction node.</summary>
    [Fact]
    public void Render_ControlFlow_ProducesNoStandaloneFlowNode()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeBranch("cond", "T", "F")),
            MakeBasic("T", MakeExit()),
            MakeBasic("F", MakeExit()));

        var bp = lens.Project(ir);

        Assert.DoesNotContain(bp.Nodes, n =>
            n.NodeType == BlueprintNodeType.BuiltinFunction &&
            n is BuiltinFunctionNode bf &&
            (bf.FunctionName == "Branch" || bf.FunctionName == "ForLoop" ||
             bf.FunctionName == "Switch" || bf.FunctionName == "Goto"));
    }

    // ── §11.2 (new — legacy deliberately skipped) ──────────────────────────

    /// <summary>§11.2: a ForLoop index renders as an EntryPointNode inside the loop body block.</summary>
    [Fact]
    public void Render_ForLoop_BodyBlockHasIndexEntryPoint()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeForLoop("0", "3", "1", "i", "LoopBody", "End")),
            MakeBasic("LoopBody", MakeGoto("End")),
            MakeBasic("End", MakeExit()));

        var bp = lens.Project(ir);

        var ep = bp.Nodes.SingleOrDefault(n => n.NodeType == BlueprintNodeType.EntryPoint);
        Assert.NotNull(ep);
        Assert.Equal("i", ep!.Name.Substring(ep.Name.IndexOf(':') + 1).Trim());
        // The loop body BlockNode should reference the EntryPoint in its ChildNodeIds.
        var body = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "LoopBody");
        Assert.Contains(ep.Id, body.ChildNodeIds);
    }

    /// <summary>§11.2: a block-local variable renders as an inner VariableNode (VariableKind.BlockVar).</summary>
    [Fact]
    public void Render_BlockVar_IsInnerVariableNode()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("Worker")),
            MakeBasicWithBlockVars("Worker", [new IrBlockVar("count", "int", "0")], MakeExit()));

        var bp = lens.Project(ir);

        var bv = bp.Nodes.OfType<VariableNode>().SingleOrDefault(v => v.VarKind == VariableKind.BlockVar);
        Assert.NotNull(bv);
        Assert.Equal("count", bv!.VarName);
        Assert.Equal("int", bv.VarType);
        var worker = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "Worker");
        Assert.Contains(bv.Id, worker.ChildNodeIds);
    }

    // ── §11.2 statement nodes in ChildNodeIds + BlockScope metadata ─────────

    /// <summary>§11.2: a non-control-flow statement node is registered in its
    /// owning BlockNode's ChildNodeIds (the gap that caused all statement nodes
    /// to appear flat on the outer canvas instead of inside their block).</summary>
    [Fact]
    public void Render_StatementNodes_AreInBlockNodeChildNodeIds()
    {
        var lens = NewLens();
        // Worker block has one Print statement + Exit terminator.
        var print = MakeCall("Print", "hello");
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("Worker")),
            MakeBasic("Worker", print, MakeExit()));

        var bp = lens.Project(ir);

        var worker = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "Worker");
        // The Print statement node id should be in Worker's ChildNodeIds.
        var printNode = bp.Nodes.OfType<BuiltinFunctionNode>()
            .Single(n => n.FunctionName == "Print");
        Assert.Contains(printNode.Id, worker.ChildNodeIds);
        // The Exit terminator should NOT be in ChildNodeIds (it emits no node).
        Assert.DoesNotContain(bp.Nodes, n => n.Name == "Exit");
    }

    /// <summary>§11.2: the BlueprintBlockScope for a named block carries the
    /// inner-node id list (NodeIds) and the owner block-node id (OwnerNodeId),
    /// so the Dashboard can rebuild ScopeBlocks and collapse/expand sub-graphs.</summary>
    [Fact]
    public void Render_BlockScope_HasNodeIdsAndOwnerNodeId()
    {
        var lens = NewLens();
        var print = MakeCall("Print", "hello");
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("Worker")),
            MakeBasicWithBlockVars("Worker",
                [new IrBlockVar("count", "int", "0")],
                print, MakeExit()));

        var bp = lens.Project(ir);

        var workerScope = bp.BlockScopes.Single(s => s.Name == "Worker");
        var workerNode = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "Worker");
        Assert.Equal(workerNode.Id, workerScope.OwnerNodeId);
        Assert.NotEmpty(workerScope.NodeIds);
        // The scope should contain at least the BlockVar node and the Print node.
        Assert.All(workerScope.NodeIds, id => Assert.Contains(id, workerNode.ChildNodeIds));
    }

    /// <summary>§11 layout: when no block has a saved "BlockPos" annotation, the
    /// auto-layout engine runs and positions block nodes at non-zero coordinates
    /// (the fix for the "all nodes at (0,0)" symptom).</summary>
    [Fact]
    public void Render_AutoLayout_PositionsBlockNodesWhenNoAnnotation()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("Worker")),
            MakeBasic("Worker", MakeExit()));

        var bp = lens.Project(ir);

        var entry = bp.Nodes.Single(n => n.NodeType == BlueprintNodeType.Entry);
        var worker = bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == "Worker");
        // At least one node should have a non-zero coordinate after auto-layout.
        Assert.True(entry.X != 0 || entry.Y != 0,
            "Entry node should be positioned by auto-layout.");
        Assert.True(worker.X != 0 || worker.Y != 0,
            "Worker block node should be positioned by auto-layout.");
    }

    /// <summary>§11 layout: when a block already has a saved "BlockPos" annotation,
    /// the auto-layout engine is skipped and the user's coordinates are preserved.</summary>
    [Fact]
    public void Render_AutoLayout_PreservesUserLayoutWhenAnnotationExists()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(
                statements: [MakeGoto("Worker")],
                annotations: [new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(500, 700))]),
            MakeBasic("Worker", MakeExit()));

        var bp = lens.Project(ir);

        var mainBlock = bp.Nodes.OfType<BlockNode>().Single(b => b.IsMainBlock);
        Assert.Equal(500.0, mainBlock.X);
        Assert.Equal(700.0, mainBlock.Y);
    }

    // ── §11.4 data edges (the SourceNodeId hack fix) ───────────────────────

    /// <summary>§11.4: a data edge into a PubVar variable node is sourced to the statement node
    /// that produced the value (NOT the legacy empty-string placeholder).</summary>
    [Fact]
    public void Render_DataEdge_SourcedToStatementNode_NotEmptyPlaceholder()
    {
        var lens = NewLens();
        // entry: `0 > x` (a pipeline writing to PubVar x), then Goto End.
        var write = MakePipelineWrite("0", "x");
        var ir = MakeWorkflow(
            globalVars: [new KeyValuePair<string, IrGlobalVar>("x", new IrGlobalVar("x", "int", null, null))],
            MakeEntry(write, MakeGoto("End")),
            MakeBasic("End", MakeExit()));

        var bp = lens.Project(ir);

        var xVar = bp.Nodes.OfType<VariableNode>().Single(v => v.VarKind == VariableKind.PubVar && v.VarName == "x");
        var dataEdge = bp.Connections.Single(c => c.TargetNodeId == xVar.Id);
        // The legacy renderer left SourceNodeId = string.Empty here. The fix: the
        // source is the statement node that wrote x, which must exist and be non-empty.
        Assert.NotEmpty(dataEdge.SourceNodeId);
        Assert.Equal("x", dataEdge.PubVarName);
        // And the source node must actually be a node in the blueprint.
        Assert.NotNull(bp.GetNodeById(dataEdge.SourceNodeId));
        Assert.Equal("stmt:", dataEdge.SourceNodeId.Substring(0, 5));
    }

    // ── layout coordinates ─────────────────────────────────────────────────

    /// <summary>Block-anchor annotation ("BlockPos") → the entry block's BlockNode X/Y
    /// (the annotation is on the IrBlock, which now maps to a BlockNode, not the
    /// synthetic EntryNode).</summary>
    [Fact]
    public void Render_BlockAnchorAnnotation_PopulatesNodeLocation()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(
            MakeEntry(
                statements: [MakeGoto("End")],
                annotations: [new IrAnnotation(AnnotationKind.Layout, "BlockPos", new IrLayout(120.5, 240))]),
            MakeBasic("End", MakeExit()));

        var bp = lens.Project(ir);

        var mainBlock = bp.Nodes.OfType<BlockNode>().Single(b => b.IsMainBlock);
        Assert.Equal(120.5, mainBlock.X);
        Assert.Equal(240.0, mainBlock.Y);
    }

    /// <summary>Per-statement layout annotation (keyed by fingerprint) → the statement node's X/Y.</summary>
    [Fact]
    public void Render_PerStatementAnnotation_PopulatesNodeLocation()
    {
        var lens = NewLens();
        var pipe = MakeCall("Print", "hello");
        var ir = MakeWorkflow(
            MakeEntry(
                statements: [pipe, MakeGoto("End")],
                annotations: [new IrAnnotation(AnnotationKind.Layout, pipe.Fingerprint.Value, new IrLayout(99, 88))]),
            MakeBasic("End", MakeExit()));

        var bp = lens.Project(ir);

        var printNode = bp.Nodes.OfType<BuiltinFunctionNode>().Single(n => n.FunctionName == "Print");
        Assert.Equal(99.0, printNode.X);
        Assert.Equal(88.0, printNode.Y);
    }

    // ── BpEditTranslator: AddNode / DeleteNode ─────────────────────────────

    /// <summary>AddNodeInBlock → IrDiff with one StatementChange(Added).</summary>
    [Fact]
    public void Translate_AddNodeInBlock_YieldsAddedStatementChange()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var ir = MakeWorkflow(MakeEntry(MakeExit()));

        var diff = translator.Translate(ir, new AddNodeInBlock("#MainBlock", "Print"));

        var added = diff.StatementChanges.Single(c => c.Kind == DiffKind.Added);
        Assert.Equal("#MainBlock", added.BlockName);
        Assert.Equal("Print", added.Fingerprint.Value.Substring(0, 5));
        Assert.NotNull(added.NewValue);
    }

    /// <summary>DeleteNode → IrDiff with one StatementChange(Removed) keyed by the OLD fingerprint.</summary>
    [Fact]
    public void Translate_DeleteNode_YieldsRemovedStatementChange()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var call = MakeCall("Print", "hello");
        var ir = MakeWorkflow(MakeEntry(call, MakeExit()));
        var nodeId = StmtNodeId("#MainBlock", 0, call);

        var diff = translator.Translate(ir, new DeleteNode(nodeId));

        var removed = diff.StatementChanges.Single(c => c.Kind == DiffKind.Removed);
        Assert.Equal("#MainBlock", removed.BlockName);
        Assert.Equal(call.Fingerprint, removed.Fingerprint);
    }

    // ── BpEditTranslator: SetNodeArgument ──────────────────────────────────

    /// <summary>SetNodeArgument → StatementChange(Modified); the fingerprint changes.</summary>
    [Fact]
    public void Translate_SetNodeArgument_YieldsModifiedStatementChange()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var call = MakeCall("Print", "hello");
        var ir = MakeWorkflow(MakeEntry(call, MakeExit()));
        var nodeId = StmtNodeId("#MainBlock", 0, call);

        var diff = translator.Translate(ir, new SetNodeArgument(nodeId, 0, "\"world\""));

        var modified = diff.StatementChanges.Single(c => c.Kind == DiffKind.Modified);
        Assert.Equal("#MainBlock", modified.BlockName);
        // New fingerprint reflects the new argument.
        Assert.Equal("Print(\"world\")", modified.Fingerprint.Value);
        Assert.NotNull(modified.NewValue);
    }

    // ── BpEditTranslator: ConnectData (§2.2 fix) ──────────────────────────

    /// <summary>
    /// §2.2 fix: ConnectData from a PubVar node to a statement's data pin sets
    /// that argument to the variable name. Print's "Value" pin is data arg 0
    /// (the Exec pin at index 0 does not count as a data argument).
    /// </summary>
    [Fact]
    public void Translate_ConnectData_SetsArgumentToVarName()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var call = MakeCall("Print", "hello");
        var ir = MakeWorkflow(
            [new("X", new IrGlobalVar("X", "string", null, null))],
            MakeEntry(call, MakeExit()));
        var stmtNodeId = StmtNodeId("#MainBlock", 0, call);
        var varNodeId = "var:X";

        var diff = translator.Translate(ir, new ConnectData(varNodeId, "Value", stmtNodeId, "Value"));

        var modified = diff.StatementChanges.Single(c => c.Kind == DiffKind.Modified);
        var pipe = Assert.IsType<IrPipelineStatement>(modified.NewValue);
        // The head FunctionCall segment's argument 0 is now the bare variable name.
        Assert.Equal("X", pipe.Segments[0].Arguments[0].Literal);
    }

    /// <summary>
    /// §2.2 fix: ConnectData targeting a non-existent pin is a no-op (empty diff),
    /// preserving the pre-fix behaviour for unknown functions / pins.
    /// </summary>
    [Fact]
    public void Translate_ConnectData_UnknownPin_IsNoOp()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var call = MakeCall("Print", "hello");
        var ir = MakeWorkflow(MakeEntry(call, MakeExit()));
        var stmtNodeId = StmtNodeId("#MainBlock", 0, call);

        var diff = translator.Translate(ir, new ConnectData("var:X", "Value", stmtNodeId, "NonExistentPin"));

        Assert.True(diff.IsEmpty);
    }

    /// <summary>
    /// §2.2 fix: RenderDataEdges draws a consumption wire (PubVar → stmt) when a
    /// statement's argument literal names a known PubVar.
    /// </summary>
    [Fact]
    public void Render_DrawsConsumptionEdge_WhenArgumentNamesPubVar()
    {
        var lens = NewLens();
        // A Print statement whose argument is the bare PubVar name "X".
        var printWithVarArg = new IrPipelineStatement
        {
            Fingerprint = IrFingerprint.Compute("Print", ["X"]),
            Sources = ["Print(X)"],
            Segments =
            [
                new IrSegment
                {
                    Kind = IrSegmentKind.FunctionCall,
                    FunctionName = "Print",
                    Arguments = [IrPipelineArgument.Lit("X")],
                },
            ],
        };
        var ir = MakeWorkflow(
            [new("X", new IrGlobalVar("X", "string", null, null))],
            MakeEntry(printWithVarArg, MakeExit()));

        var bp = lens.Project(ir);

        // Expect a connection from the PubVar node (var:X) to the statement node.
        Assert.Contains(bp.Connections, c =>
            c.SourceNodeId == "var:X" && c.TargetNodeId.StartsWith("stmt:"));
    }

    // ── BpEditTranslator: SetControlFlowArm ────────────────────────────────

    /// <summary>SetControlFlowArm → StatementChange(Modified) with the arm's target updated.</summary>
    [Fact]
    public void Translate_SetControlFlowArm_YieldsModifiedWithUpdatedArm()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var branch = MakeBranch("cond", "T", "F");
        var ir = MakeWorkflow(
            MakeEntry(branch),
            MakeBasic("T", MakeExit()),
            MakeBasic("F", MakeExit()));
        // A control-flow terminator is addressed via the BLOCK node id.
        var blockId = "block:#MainBlock";

        var diff = translator.Translate(ir, new SetControlFlowArm(blockId, "True", "NewTarget"));

        var modified = diff.StatementChanges.Single(c => c.Kind == DiffKind.Modified);
        var cf = Assert.IsType<IrControlFlowStatement>(modified.NewValue);
        Assert.Equal("NewTarget", cf.Targets.Single(t => t.PinName == "True").TargetBlockName);
        // The False arm is untouched.
        Assert.Equal("F", cf.Targets.Single(t => t.PinName == "False").TargetBlockName);
    }

    // ── BpEditTranslator: AddBlock / DeleteBlock ───────────────────────────

    /// <summary>AddBlock → IrDiff with one BlockChange(Added).</summary>
    [Fact]
    public void Translate_AddBlock_YieldsAddedBlockChange()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var ir = MakeWorkflow(MakeEntry(MakeExit()));

        var diff = translator.Translate(ir, new AddBlock("NewBlock"));

        var added = diff.BlockChanges.Single(c => c.Kind == BlockChangeKind.Added);
        Assert.Equal("NewBlock", added.Name);
        Assert.NotNull(added.NewBlock);
    }

    /// <summary>DeleteBlock → IrDiff with one BlockChange(Removed).</summary>
    [Fact]
    public void Translate_DeleteBlock_YieldsRemovedBlockChange()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var ir = MakeWorkflow(
            MakeEntry(MakeGoto("Extra")),
            MakeBasic("Extra", MakeExit()));

        var diff = translator.Translate(ir, new DeleteBlock("Extra"));

        var removed = diff.BlockChanges.Single(c => c.Kind == BlockChangeKind.Removed);
        Assert.Equal("Extra", removed.Name);
    }

    // ── BpEditTranslator: MoveNodePosition (pure view state) ───────────────

    /// <summary>MoveNodePosition yields an empty IrDiff (view state is not encoded in IrDiff),
    /// and ApplyPosition persists the coordinate into the baseline's annotations.</summary>
    [Fact]
    public void Translate_MoveNodePosition_YieldsEmptyDiff_AndApplyPositionPersists()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var ir = MakeWorkflow(MakeEntry(MakeExit()));

        // Pure semantic diff is empty (coordinate-only move).
        var diff = translator.Translate(ir, new MoveNodePosition("block:#MainBlock", 42, 7));
        Assert.True(diff.IsEmpty);

        // ApplyPosition persists the coordinate.
        var updated = BpEditTranslator.ApplyPosition(ir, "block:#MainBlock", 42, 7);
        var anchor = updated.EntryBlock.Annotations.Single(a => a.Kind == AnnotationKind.Layout && a.Key == "BlockPos");
        var coords = (IrLayout)anchor.Value!;
        Assert.Equal(42.0, coords.X);
        Assert.Equal(7.0, coords.Y);
        // Baseline is untouched (purity).
        Assert.DoesNotContain(ir.EntryBlock.Annotations, a => a.Key == "BlockPos");
    }

    // ── Hardcoded-dictionary guard (the core smell being eliminated) ───────

    /// <summary>BpEditTranslator has NO DashboardToCfgName field; BP-name → IR-name goes
    /// through the registry. Verifying via reflection so a future edit cannot
    /// silently reintroduce the hardcoded map.</summary>
    [Fact]
    public void BpEditTranslator_HasNoDashboardToCfgNameDictionary()
    {
        var translatorType = typeof(BpEditTranslator);
        // No field of any name contains "DashboardToCfgName" (the legacy smell).
        var offending = translatorType.GetFields(System.Reflection.BindingFlags.Instance |
                                                  System.Reflection.BindingFlags.Static |
                                                  System.Reflection.BindingFlags.NonPublic |
                                                  System.Reflection.BindingFlags.Public)
            .Where(f => f.Name.IndexOf("DashboardToCfgName", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        Assert.Empty(offending);

        // And no field whose type is Dictionary<string,string> (the legacy shape).
        var dictFields = translatorType.GetFields(System.Reflection.BindingFlags.Instance |
                                                   System.Reflection.BindingFlags.Static |
                                                   System.Reflection.BindingFlags.NonPublic |
                                                   System.Reflection.BindingFlags.Public)
            .Where(f => f.FieldType == typeof(Dictionary<string, string>))
            .ToList();
        Assert.Empty(dictFields);
    }

    /// <summary>The ForLoop alias "Loop" resolves to "ForLoop" via the registry
    /// (replacing the legacy DashboardToCfgName["Loop"] entry).</summary>
    [Fact]
    public void ResolveBpName_LoopAlias_ResolvesToForLoopViaRegistry()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        Assert.Equal("ForLoop", translator.ResolveBpName("Loop"));
    }

    /// <summary>An unknown alias (BP name == IR name) resolves to itself (identity fallback).</summary>
    [Fact]
    public void ResolveBpName_UnknownAlias_ResolvesToIdentity()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        Assert.Equal("Print", translator.ResolveBpName("Print"));
        Assert.Equal("JsonGetField", translator.ResolveBpName("JsonGetField"));
    }

    /// <summary>AddNodeInBlock with the "Loop" BP alias produces a ForLoop IR statement
    /// (end-to-end: registry resolution drives the translated statement's op).</summary>
    [Fact]
    public void Translate_AddNode_LoopAlias_ProducesForLoopStatement()
    {
        var translator = new BpEditTranslator(BuildRegistry());
        var ir = MakeWorkflow(MakeEntry(MakeExit()));

        var diff = translator.Translate(ir, new AddNodeInBlock("#MainBlock", "Loop"));

        var added = diff.StatementChanges.Single(c => c.Kind == DiffKind.Added);
        var cf = Assert.IsType<IrControlFlowStatement>(added.NewValue);
        Assert.Equal(ControlFlowOp.ForLoop, cf.Op);
        Assert.Equal("ForLoop", cf.FunctionName);
    }

    // ── End-to-end: Diff → Apply round-trip ────────────────────────────────

    /// <summary>Translating a BP edit to an IrDiff and applying it yields the expected new IR.</summary>
    [Fact]
    public void Diff_ThenApply_RoundTrips_AddedStatement()
    {
        var lens = NewLens();
        var ir = MakeWorkflow(MakeEntry(MakeExit()));

        // AddNodeInBlock with no Position appends; specify index 0 to insert before Exit.
        var diff = lens.Diff(ir, new AddNodeInBlock("#MainBlock", "Print", Position: 0));
        var newIr = IrDiffApply.Apply(ir, diff);

        // The entry block now has Print at index 0 followed by the Exit terminator.
        Assert.Equal(2, newIr.EntryBlock.Statements.Length);
        Assert.Equal("Print", ((IrPipelineStatement)newIr.EntryBlock.Statements[0]).Segments[0].FunctionName);
        Assert.Equal(ControlFlowOp.Exit, ((IrControlFlowStatement)newIr.EntryBlock.Statements[1]).Op);
    }

    // ── pin / block lookup helpers ─────────────────────────────────────────

    private static string BlockId(BP bp, string name) =>
        bp.Nodes.OfType<BlockNode>().Single(b => b.BlockName == name).Id;

    private static string? PinName(BP bp, string nodeId, string pinId) =>
        bp.GetNodeById(nodeId)?.GetPinById(pinId)?.Name;

    private static PinType? PinTypeOf(BP bp, string nodeId, string pinId) =>
        bp.GetNodeById(nodeId)?.GetPinById(pinId)?.Type;

    private static bool HasExecEdge(BP bp, string pinName, string targetNodeId) =>
        bp.Connections.Any(c =>
            c.TargetNodeId == targetNodeId &&
            PinName(bp, c.SourceNodeId, c.SourcePinId) == pinName &&
            PinTypeOf(bp, c.SourceNodeId, c.SourcePinId) == PinType.Execution);

    // ── IR construction helpers (mirror IrDiffTests' minimal builders) ─────

    private static string StmtNodeId(string block, int ordinal, IrStatement stmt) =>
        "stmt:" + IrFingerprint.DeriveStableId(block, stmt.Fingerprint, ordinal);

    /// <summary>A bare-call pipeline `Func("arg")`.</summary>
    private static IrPipelineStatement MakeCall(string func, string arg)
    {
        var fp = IrFingerprint.Compute(func, [$"\"{arg}\""]);
        return new IrPipelineStatement
        {
            Fingerprint = fp,
            Sources = [$"{func}(\"{arg}\")"],
            Segments =
            [
                new IrSegment
                {
                    Kind = IrSegmentKind.FunctionCall,
                    FunctionName = func,
                    Arguments = [IrPipelineArgument.Lit($"\"{arg}\"")],
                },
            ],
        };
    }

    /// <summary>A pipeline `source > varName` (writes to a PubVar), with two segments.</summary>
    private static IrPipelineStatement MakePipelineWrite(string source, string varName)
    {
        var args = new[] { source };
        var fp = IrFingerprint.Compute("__pipeline", args, varName);
        return new IrPipelineStatement
        {
            Fingerprint = fp,
            Sources = [source],
            Segments =
            [
                new IrSegment { Kind = IrSegmentKind.FunctionCall, FunctionName = "__pipeline", Arguments = [] },
                new IrSegment { Kind = IrSegmentKind.Variable, VariableName = varName },
            ],
        };
    }

    private static IrControlFlowStatement MakeGoto(string target) => new()
    {
        Fingerprint = IrFingerprint.Compute("Goto", [$"\"{target}\""]),
        Op = ControlFlowOp.Goto,
        FunctionName = "Goto",
        Arguments = [$"\"{target}\""],
        Targets = [new IrControlFlowTarget("Exec", target)],
    };

    private static IrControlFlowStatement MakeExit() => new()
    {
        Fingerprint = IrFingerprint.Compute("Exit", []),
        Op = ControlFlowOp.Exit,
        FunctionName = "Exit",
        Arguments = [],
        Targets = [],
    };

    private static IrControlFlowStatement MakeBranch(string cond, string tBlock, string fBlock) => new()
    {
        Fingerprint = IrFingerprint.Compute("Branch", [cond]),
        Op = ControlFlowOp.Branch,
        FunctionName = "Branch",
        Arguments = [cond],
        Targets =
        [
            new IrControlFlowTarget("True", tBlock),
            new IrControlFlowTarget("False", fBlock),
        ],
    };

    private static IrControlFlowStatement MakeForLoop(string from, string to, string step, string index, string body, string end) => new()
    {
        Fingerprint = IrFingerprint.Compute("ForLoop", [from, to, step, index, body, end]),
        Op = ControlFlowOp.ForLoop,
        FunctionName = "ForLoop",
        Arguments = [from, to, step, index, body, end],
        Targets =
        [
            new IrControlFlowTarget("LoopBody", body),
            new IrControlFlowTarget("LoopEnd", end),
        ],
    };

    private static IrControlFlowStatement MakeSwitch(string selector, params string[] blocks)
    {
        var args = new List<string> { selector };
        var targets = new List<IrControlFlowTarget>();
        for (int i = 0; i < blocks.Length; i++)
        {
            args.Add(blocks[i]);
            targets.Add(new IrControlFlowTarget(i == 0 ? "Default" : (i - 1).ToString(), blocks[i]));
        }
        return new IrControlFlowStatement
        {
            Fingerprint = IrFingerprint.Compute("Switch", args),
            Op = ControlFlowOp.Switch,
            FunctionName = "Switch",
            Arguments = args.ToImmutableArray(),
            Targets = targets.ToImmutableArray(),
        };
    }

    private static IrBlock MakeEntry(
        ImmutableArray<IrStatement>? statements = null,
        ImmutableArray<IrAnnotation>? annotations = null,
        ImmutableArray<IrEdge>? successors = null) => new()
        {
            Name = "#MainBlock",
            Kind = IrBlockKind.Entry,
            Statements = statements ?? [],
            Annotations = annotations ?? [],
            Successors = successors ?? [],
        };

    private static IrBlock MakeEntry(params IrStatement[] statements) =>
        MakeEntry(statements.ToImmutableArray());

    private static IrBlock MakeBasic(string name, params IrStatement[] statements) => new()
    {
        Name = name,
        Kind = IrBlockKind.Basic,
        Statements = statements.ToImmutableArray(),
    };

    private static IrBlock MakeBasicWithBlockVars(string name, IReadOnlyList<IrBlockVar> blockVars, params IrStatement[] statements) => new()
    {
        Name = name,
        Kind = IrBlockKind.Basic,
        Statements = statements.ToImmutableArray(),
        BlockVars = blockVars.ToImmutableArray(),
    };

    private static IrWorkflow MakeWorkflow(
        params IrBlock[] blocks) => new()
        {
            MainBlockName = "#MainBlock",
            Blocks = blocks.ToImmutableArray(),
        };

    private static IrWorkflow MakeWorkflow(
        IReadOnlyList<KeyValuePair<string, IrGlobalVar>> globalVars,
        params IrBlock[] blocks) => new()
        {
            MainBlockName = "#MainBlock",
            Blocks = blocks.ToImmutableArray(),
            GlobalVars = globalVars.ToImmutableDictionary(),
        };
}
