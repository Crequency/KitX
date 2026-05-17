using System;
using KitX.Core.Workflow.Blueprint.CFG;
using System.Collections.Generic;

using static KitX.Core.Workflow.BlockScripting.BlockScriptWellKnown.Pins;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.Pipeline;

/// <summary>
/// Accumulated state flowing through all 6 conversion phases.
/// Replaces the old ConversionContext.
/// </summary>
public class PipelineContext
{
    // --- Input ---
    public required BlockScript Script { get; set; }
    public List<HelperFunction> HelperFunctions { get; set; } = [];

    // --- Phase 1 output ---
    public Dictionary<string, ConstNode> ConstNodes { get; set; } = new();
    public Dictionary<string, VariableNode> VariableNodes { get; set; } = new();
    public List<string> PubVarNames { get; set; } = new();

    // --- Phase 2 output ---
    public ControlFlowGraph FormattedScript { get; set; } = new();

    // --- Phase 3 output ---
    public List<BlueprintNode> AllNodes { get; set; } = new();
    public EntryNode? EntryNode { get; set; }
    public Dictionary<string, BlueprintNode> NodeByStatementId { get; set; } = new();
    public Dictionary<string, BlueprintNode> BlockFirstNodes { get; set; } = new();
    public List<PendingExecEdge> ExecEdges { get; set; } = new();

    // --- Block scope tracking (populated by CFG2BPConverter, consumed by Assembler) ---
    public Dictionary<string, List<string>> BlockNodeIds { get; set; } = new();
    public Dictionary<string, string?> BlockNextBlock { get; set; } = new();
    public Dictionary<string, bool> BlockEndsWithFlowCtrl { get; set; } = new();
    public List<(string stmtId, string? trueBlock, string? falseBlock)> BranchDefs { get; set; } = new();
    public List<(string stmtId, string? loopBody, string? loopEnd, string parentBlock)> LoopDefs { get; set; } = new();

    // --- Phase 4 output ---
    public List<PendingDataEdge> DataEdges { get; set; } = new();

    // --- Generic control flow deferred edges (populated by IBuiltinFunctionDefinition.OnNodeCreated) ---
    public List<DeferredControlFlowEdge> DeferredEdges { get; set; } = new();

    // --- Shared state ---
    public int NextPubVarCounter { get; set; } = 0;
    public HashSet<string> VisitedBlocks { get; set; } = new();

    /// <summary>
    /// PubVar reuse tracking: fingerprint → assignment info.
    /// When the same expression is assigned to the same PubVar in multiple blocks,
    /// the nodes are shared (not re-created).
    /// </summary>
    public Dictionary<string, PubVarAssignment> PubVarAssignments { get; set; } = new();

    /// <summary>
    /// parentBlockName → LoopNode (for ToLoopCond resolution)
    /// </summary>
    public Dictionary<string, BlueprintNode> LoopNodesByParent { get; set; } = new();

    /// <summary>
    /// parentBlockName → condition PubVar assignment info (for Loop condition duplication)
    /// </summary>
    public Dictionary<string, ConditionInfo> LoopConditions { get; set; } = new();

    // --- Debug tracking ---
    public List<string> DebugLog { get; set; } = new();
}

/// <summary>
/// Tracks a PubVar assignment for reuse detection.
/// Key = expression fingerprint (e.g. "HelperFuncCompare(BLE,Get(currentLoop),loopMax)").
/// </summary>
public class PubVarAssignment
{
    public string PubVarName { get; set; } = string.Empty;
    public BlueprintNode SourceNode { get; set; } = null!;
    public BlueprintPin SourcePin { get; set; } = null!;
    public string StatementId { get; set; } = string.Empty;

    /// <summary>
    /// Sub-assignments that are part of this expression (e.g. Get nodes that feed into a CallHelper).
    /// Key = sub-expression fingerprint, Value = (node, pin, statementId).
    /// </summary>
    public Dictionary<string, SubAssignment> SubAssignments { get; set; } = new();
}

/// <summary>
/// Sub-assignment within a PubVar expression chain.
/// </summary>
public class SubAssignment
{
    public BlueprintNode Node { get; set; } = null!;
    public BlueprintPin Pin { get; set; } = null!;
    public string StatementId { get; set; } = string.Empty;
}

/// <summary>
/// Information about a Loop's condition for duplication before ToLoopCond.
/// </summary>
public class ConditionInfo
{
    /// <summary>
    /// The PubVar that holds the condition result (e.g. "vaaa0001").
    /// </summary>
    public string ConditionPubVar { get; set; } = string.Empty;

    /// <summary>
    /// The expanded condition expression (e.g. "HelperFuncCompare(\"BLE\", Get(\"currentLoop\"), loopMax)").
    /// </summary>
    public string RawExpression { get; set; } = string.Empty;

    /// <summary>
    /// The fingerprint of the condition expression for reuse detection.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>
    /// The formatted statements that evaluate the condition (may be multiple if nested).
    /// The last one assigns to ConditionPubVar.
    /// </summary>
    public List<CFGStatement> ExpansionStatements { get; set; } = new();
}

/// <summary>
/// A pending exec edge to be created between two nodes.
/// </summary>
public class PendingExecEdge
{
    public string SourceStatementId { get; set; } = string.Empty;
    public string TargetStatementId { get; set; } = string.Empty;
    public string SourcePinName { get; set; } = Exec;
    public string TargetPinName { get; set; } = Exec;

    // For special routing (Branch.True, Branch.False, Loop.LoopBody, Loop.LoopEnd)
    public bool IsSpecialRouting { get; set; }
}

/// <summary>
/// A pending data edge to be created between two pins.
/// </summary>
public class PendingDataEdge
{
    public string SourceNodeId { get; set; } = string.Empty;
    public string SourcePinName { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public string TargetPinName { get; set; } = string.Empty;
    public string? PubVarName { get; set; }
}

/// <summary>
/// Generic deferred control flow edge for IBuiltinFunctionDefinition-based functions.
/// Replaces per-function BranchDefs/LoopDefs with a unified structure.
/// Populated by OnNodeCreated, resolved by CFG2BPConverter.ResolveCrossBlockEdges.
/// </summary>
public struct DeferredControlFlowEdge
{
    /// <summary>Source statement ID (the control flow node)</summary>
    public string SourceStatementId { get; set; }

    /// <summary>Output arms: each defines a pin name and target block name</summary>
    public List<(string PinName, string TargetBlockName)> Arms { get; set; }

    /// <summary>For ToLoopCond-style loopback: the block to return to. Null for non-loopback edges.</summary>
    public string? LoopbackTargetBlock { get; set; }
}
