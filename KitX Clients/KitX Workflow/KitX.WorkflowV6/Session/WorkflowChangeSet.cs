namespace KitX.WorkflowV6.Session;

using KitX.WorkflowV6.Diff;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowChangeSet — the session-level description of one IR change, surfaced to
// renderers and the host so each side can do a focused re-render.
//
// Inherited contract from KitX.WorkflowIR.Session.IrChangeSet: carries the semantic
// diff plus the derived list of lexical paths whose rendered view changed. Immutable
// record; built by the SyncService after applying a WorkflowDiff.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal description of one IR change. The contract between SyncService and
/// renderers: carries the semantic diff plus the derived list of lexical paths
/// whose rendered view changed.
/// </summary>
public sealed record WorkflowChangeSet
{
    /// <summary>The statement-level semantic diff, or null if no structural change.</summary>
    public WorkflowDiff? StatementDiff { get; init; }

    /// <summary>
    /// Lexical paths affected by this change (for focused re-rendering). A path
    /// appears here if any of its descendants changed, or if a statement at that path
    /// was added/removed/modified.
    /// </summary>
    public IReadOnlyList<string> AffectedPaths { get; init; } = [];

    /// <summary>Builds the affected-path list from a WorkflowDiff.</summary>
    public static IReadOnlyList<string> CollectAffectedPaths(WorkflowDiff diff)
    {
        var paths = new HashSet<string>();
        foreach (var c in diff.StatementChanges)
        {
            paths.Add(c.LexicalPath);
            // Also mark the parent path so a containing scope re-renders.
            var slash = c.LexicalPath.LastIndexOf('/');
            if (slash > 0) paths.Add(c.LexicalPath[..slash]);
        }
        return paths.ToArray();
    }
}
