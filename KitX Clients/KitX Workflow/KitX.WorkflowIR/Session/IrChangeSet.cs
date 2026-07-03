namespace KitX.Workflow.Session;

using KitX.Workflow.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrChangeSet — the session-level description of one IR change, surfaced to
// renderers and the host so each side can do a focused re-render.
//
// Replaces the legacy CfgChangeSet (a mutable class mixing StatementDiff +
// AffectedBlocks + PositionsChanged). Here it is an immutable record carrying
// the IrDiff plus the derived affected-block list and the PositionsChanged flag.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal description of one IR change. The contract between SyncService and
/// renderers: carries the semantic diff plus the derived list of blocks whose
/// rendered view changed and whether positions moved.
/// </summary>
public sealed record IrChangeSet
{
    /// <summary>The statement/block-level semantic diff, or null if no structural change.</summary>
    public IrDiff? StatementDiff { get; init; }

    /// <summary>Names of blocks affected by this change (for focused re-rendering).</summary>
    public IReadOnlyList<string> AffectedBlocks { get; init; } = [];

    /// <summary>True when one or more nodes moved (position-only change, no structural diff).</summary>
    public bool PositionsChanged { get; init; }

    /// <summary>Builds the affected-block list from an IrDiff.</summary>
    public static IReadOnlyList<string> CollectAffectedBlocks(IrDiff diff)
    {
        var blocks = new HashSet<string>();
        foreach (var c in diff.StatementChanges)
        {
            blocks.Add(c.BlockName);
            if (c.ToBlock is not null) blocks.Add(c.ToBlock);
        }
        foreach (var bc in diff.BlockChanges) blocks.Add(bc.Name);
        return blocks.ToArray();
    }
}
