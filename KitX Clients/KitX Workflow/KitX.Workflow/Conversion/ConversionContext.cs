using KitX.Core.Contract.Workflow;

using KitX.Workflow.CFG;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Blueprint;
namespace KitX.Workflow.Conversion;

/// <summary>
/// Accumulated state for Blueprint → BlockScript conversion.
/// Shared across all CFG pipeline phases.
/// </summary>
internal class ConversionContext
{
    public required KitX.Core.Contract.Workflow.Blueprint Blueprint { get; set; }
    public required BlockScript Script { get; set; }

    // Phase 1: Connection analysis
    public Dictionary<string, BlueprintNode> NodeById { get; set; } = new();
    public List<BlueprintConnection> ExecConnections { get; set; } = new();
    public List<BlueprintConnection> DataConnections { get; set; } = new();
    public Dictionary<(string nodeId, string pinName), DataEdgeInfo> InputDataMap { get; set; } = new();
    public HashSet<(string nodeId, string pinName)> ConsumedOutputs { get; set; } = new();
    public List<string> AllPubVars { get; set; } = new();
    public List<string> AutoPubVars { get; set; } = new();
    public int PubVarCounter { get; set; } = 1;

    // Phase 2: CFG block construction
    public Dictionary<string, BlueprintNode> LoopNodes { get; set; } = new();

    /// <summary>
    /// Current loopback target ID when walking inside a loop body.
    /// </summary>
    public string? CurrentLoopbackTargetId { get; set; }

    /// <summary>
    /// Maps loop node ID to the block name that contains the Loop statement.
    /// </summary>
    public Dictionary<string, string> LoopOwnerBlockNames { get; set; } = new();

    /// <summary>
    /// User-facing diagnostics accumulated across BP→CFG→BS phases.
    /// </summary>
    public ConversionDiagnostics Diagnostics { get; set; } = new();
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