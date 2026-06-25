using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Renders a <see cref="ControlFlowGraph"/> into a Blueprint graph (the v5.1 G-3 path:
/// CFG is the single source of truth, BP is a rendered view).
/// </summary>
/// <remarks>
/// RED-LIGHT CONTRACT (not yet implemented). This interface is the seam the Dashboard's
/// Blueprint editor will consume instead of the deleted CFG2BPConverter. Implementation
/// is deferred; tests against this interface are expected to fail until the renderer ships.
///
/// Rendering rules (per <c>Package/BlockScriptGrammarRule.md</c> §11):
/// <list type="bullet">
///   <item>MainBlock → EntryNode + its first statements.</item>
///   <item>Pipeline <c>a &gt; b &gt; c</c> → data edges between the materialised nodes.</item>
///   <item>Branch/ForLoop/Switch → block node with Exec arms (CFGEdgeType.BranchTrue/...).</item>
///   <item><c>#Block X</c> → a compound BlockNode (foldable in the UI) with Entry/Exit ports.</item>
///   <item>Node positions read from CFG LayoutX/Y when present (position preservation).</item>
/// </list>
/// </remarks>
public interface ICFGGraphRenderer
{
    /// <summary>
    /// Render the CFG into a Blueprint graph (nodes + connections).
    /// </summary>
    KitX.Core.Contract.Workflow.Blueprint Render(ControlFlowGraph cfg);
}
