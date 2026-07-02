namespace KitX.Workflow.Conversion;

/// <summary>
/// Minimal description of one CFG change. The contract between sync services
/// (IBsSyncService / IBpEditApplier) and renderers (ICfgBsRenderer / ICfgBpRenderer).
/// Carries the semantic diff so each side can do a focused re-render.
/// </summary>
public class CfgChangeSet
{
    /// <summary>The statement/block-level semantic diff, or null if no structural change.</summary>
    public CfgDiff? StatementDiff { get; set; }

    /// <summary>Names of blocks affected by this change (for focused re-rendering).</summary>
    public List<string> AffectedBlocks { get; set; } = [];

    /// <summary>True when one or more nodes moved (position-only change, no structural diff).</summary>
    public bool PositionsChanged { get; set; }
}
