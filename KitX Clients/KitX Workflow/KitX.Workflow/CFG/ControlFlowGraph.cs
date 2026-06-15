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

    public BlueprintDebugContext? DebugContext { get; set; }

    /// <summary>
    /// Gets a block by name. Returns null if not found.
    /// </summary>
    public CFGBlock? GetBlock(string name)
        => Blocks.FirstOrDefault(b => b.Name == name);

    /// <summary>
    /// Generates the next unique PubVar name.
    /// </summary>
    public string NextPubVarName()
        => $"vaaa{PubVarCounter++:D4}";

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
            sb.AppendLine($"  ── Block \"{block.Name}\" (Type={block.Type}, IsMain={block.IsMainBlock}, NextBlock={block.NextBlockName ?? "null"}, ParentLoop={block.ParentLoopBlockName ?? "null"})");
            foreach (var stmt in block.Statements)
            {
                var dup = stmt.IsLoopConditionDuplication ? " [COND_DUP]" : "";
                sb.AppendLine($"    [{stmt.Kind}] {stmt.OriginalExpression}{dup}");
                if (!string.IsNullOrEmpty(stmt.TrueBlockName))
                    sb.AppendLine($"      → True=\"{stmt.TrueBlockName}\", False=\"{stmt.FalseBlockName}\"");
                if (!string.IsNullOrEmpty(stmt.ToLoopCondReturnTo))
                    sb.AppendLine($"      → ToLoopCond=\"{stmt.ToLoopCondReturnTo}\"");
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