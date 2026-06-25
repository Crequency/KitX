using KitX.Core.Contract.Workflow;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Mutable state flowing through the BS→CFG conversion pipeline.
/// v5.1 cleanup: the BP-assembly phase state (Phase 3/4 — AllNodes, ExecEdges, DataEdges,
/// DeferredEdges, PubVarAssignments, LoopNodesByParent, ReachingDefinitions, etc.) was removed
/// because the BP round-trip path is gone (BP is now a rendered view of CFG). Only the fields
/// consumed by the live BS→CFG→CS path remain.
/// </summary>
public class ForwardConversionState
{
    // --- Input ---
    public required BlockScript Script { get; set; }
    public List<HelperFunction> HelperFunctions { get; set; } = [];

    // --- Phase 1 output ---
    public Dictionary<string, ConstNode> ConstNodes { get; set; } = new();
    public Dictionary<string, VariableNode> VariableNodes { get; set; } = new();
    public List<string> PubVarNames { get; set; } = [];

    // --- Phase 2 output ---
    public ControlFlowGraph FormattedScript { get; set; } = new();

    // --- Shared state ---
    public int NextPubVarCounter { get; set; } = 0;

    /// <summary>
    /// User-facing diagnostics accumulated across BS→CFG phases. Backend-bug-class
    /// problems go to Serilog instead and are NOT collected here.
    /// </summary>
    public ConversionDiagnostics Diagnostics { get; set; } = new();

    /// <summary>
    /// v5.1: variable names injected at runtime by flow-control functions
    /// (e.g. ForLoop indexName). Populated after Phase 2 (CFG is available),
    /// consumed by C# codegen to emit <c>G.Get("name")</c> for these references.
    /// </summary>
    public HashSet<string> InjectedVariableNames { get; set; } = new();
}
