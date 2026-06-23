using KitX.Core.Contract.Workflow;
using System.Text;

namespace KitX.Workflow.CFG;

/// <summary>
/// The Control Flow Graph — canonical intermediate representation for
/// both BS→BP and BP→BS conversion. Both directions produce/consume
/// a CFG, ensuring round-trip fidelity by structural equivalence.
/// </summary>
public class ControlFlowGraph
{
    /// <summary>
    /// Main block name (always first in Blocks list). Computed from EntryBlock.
    /// </summary>
    public string MainBlockName { get; set; } = "MainBlock";

    /// <summary>
    /// All blocks in the CFG, ordered: MainBlock first, then named blocks
    /// in definition order.
    /// </summary>
    public List<CFGBlock> Blocks { get; set; } = [];

    /// <summary>
    /// The entry block (MainBlock equivalent).
    /// </summary>
    public CFGBlock EntryBlock { get; set; } = null!;

    /// <summary>
    /// Public variable names declared in #PubVarBlock.
    /// </summary>
    public List<string> PubVarDeclarations { get; set; } = [];

    /// <summary>
    /// PubVar types keyed by variable name. Populated from the parsed #PubVarBlock
    /// (BS→CFG) or from VariableNode.VarType (BP→CFG). Used by CFG2BS to restore
    /// strong types in round-trip. Capacitor variables (vaaa####) are NOT in this map.
    /// </summary>
    public Dictionary<string, string> PubVarTypes { get; set; } = [];

    /// <summary>
    /// Constant declarations from #ConstBlock.
    /// </summary>
    public List<ConstDeclaration> ConstDeclarations { get; set; } = [];

    /// <summary>
    /// Helper functions available in the script.
    /// </summary>
    public List<HelperFunction> HelperFunctions { get; set; } = [];

    /// <summary>
    /// Counter for generating unique PubVar names (vaaa0001, vaaa0002, ...).
    /// </summary>
    public int PubVarCounter { get; set; } = 1;

    /// <summary>
    /// Debug mapping from CFG statement IDs to Blueprint node IDs.
    /// Populated during BP→CFG / CFG→BP conversion for use by the debug execution pipeline.
    /// Null when no debug context was produced. (Inlined from the former BlueprintDebugContext
    /// 1-field wrapper; the null guard distinguishes "no mapping" from "empty mapping".)
    /// </summary>
    public Dictionary<string, string>? DebugStatementToNodeId { get; set; }

    /// <summary>
    /// Dumps the CFG as a human-readable string for diagnostics.
    /// </summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  CFG: {Blocks.Count} blocks, entry={EntryBlock?.Name}");
        sb.AppendLine($"  PubVars: [{string.Join(", ", PubVarDeclarations)}]");
        sb.AppendLine($"  Consts: {ConstDeclarations.Count}");

        foreach (var block in Blocks)
        {
            sb.AppendLine($"  ── Block \"{block.Name}\" (Type={block.Type}, IsMain={block.IsMainBlock}, FallThrough={block.FallThroughTarget ?? "null"})");
            foreach (var stmt in block.GetEffectiveStatements())
            {
                sb.AppendLine($"    [{stmt.Kind}] {stmt.OriginalExpression}");
                if (!string.IsNullOrEmpty(stmt.TrueBlockName))
                    sb.AppendLine($"      → True=\"{stmt.TrueBlockName}\", False=\"{stmt.FalseBlockName}\"");
                if (!string.IsNullOrEmpty(stmt.PubVarTarget))
                    sb.AppendLine($"      PubVarTarget={stmt.PubVarTarget}");
            }

            if (block.Successors.Count > 0)
            {
                sb.AppendLine($"    Edges:");
                foreach (var edge in block.Successors)
                    sb.AppendLine($"      {edge.Type}: {edge.FromBlockName} → {edge.ToBlockName} (pin={edge.PinName ?? "-"})");
            }
        }

        return sb.ToString();
    }
}