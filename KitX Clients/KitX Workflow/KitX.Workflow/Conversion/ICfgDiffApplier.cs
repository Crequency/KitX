using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Applies a <see cref="CfgDiff"/> to a live <see cref="ControlFlowGraph"/>,
/// mutating blocks, statements, and successors in place.
/// </summary>
/// <remarks>
/// This is the "execute" half of the diff pipeline. <see cref="ICFGDiffer.Diff"/>
/// computes the structural delta; this interface applies it.
/// </remarks>
public interface ICfgDiffApplier
{
    void Apply(CfgDiff diff, ControlFlowGraph liveCfg);
}
