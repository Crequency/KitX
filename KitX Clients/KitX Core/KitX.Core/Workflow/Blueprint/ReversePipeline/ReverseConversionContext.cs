using System.Collections.Generic;
using KitX.Core.Contract.Workflow;

namespace KitX.Core.Workflow.Blueprint.ReversePipeline;

/// <summary>
/// Accumulated state for the reverse conversion (Blueprint → BlockScript).
/// Shared across all reverse pipeline phases.
/// </summary>
internal class ReverseConversionContext
{
    public required Contract.Workflow.Blueprint Blueprint { get; set; }
    public required BlockScript Script { get; set; }

    // Phase 1: Analysis
    public Dictionary<string, BlueprintNode> NodeById { get; set; } = new();
    public List<BlueprintConnection> ExecConnections { get; set; } = new();
    public List<BlueprintConnection> DataConnections { get; set; } = new();
    public Dictionary<(string nodeId, string pinName), DataEdgeInfo> InputDataMap { get; set; } = new();
    public HashSet<(string nodeId, string pinName)> ConsumedOutputs { get; set; } = new();
    public List<string> AllPubVars { get; set; } = new();
    public List<string> AutoPubVars { get; set; } = new();
    public int PubVarCounter { get; set; } = 1;
    public int BlockCounter { get; set; } = 0;

    // Phase 2: Execution walk
    public Dictionary<string, FlowControlStatement> ControlFlowMap { get; set; } = new();
    public List<BlueprintNode> PendingControlFlowNodes { get; set; } = new();
    public Dictionary<string, BlueprintNode> LoopNodes { get; set; } = new();
    public Dictionary<string, BlockDefinition> LoopBodyBlocks { get; set; } = new();
    public Dictionary<string, (string TrueBlockName, string FalseBlockName)> BlockNameAssignments { get; set; } = new();

    // Block scope fast path
    public Dictionary<string, BlueprintBlockScope> ScopesByName { get; set; } = new();

    /// <summary>
    /// Current loopback target ID when walking inside a loop body.
    /// Set by ProcessLoopSubGraphs, read by ProcessBranchSubGraphs.
    /// </summary>
    public string? CurrentLoopbackTargetId { get; set; }

    /// <summary>
    /// Maps loop node ID to the block name that contains the Loop statement.
    /// Used during topology-based reverse walk to generate correct ToLoopCond arguments.
    /// </summary>
    public Dictionary<string, string> LoopOwnerBlockNames { get; set; } = new();
}

/// <summary>
/// Information about a data edge for input resolution.
/// </summary>
internal class DataEdgeInfo
{
    public BlueprintNode SourceNode { get; set; } = null!;
    public string SourcePinName { get; set; } = string.Empty;
    public BlueprintPin SourcePin { get; set; } = null!;
    public string? PubVarName { get; set; }
    public BlueprintConnection Connection { get; set; } = null!;
}
