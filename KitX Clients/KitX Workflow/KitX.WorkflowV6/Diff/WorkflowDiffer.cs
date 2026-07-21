namespace KitX.WorkflowV6.Diff;

using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowDiffer — the semantic diff engine for the structured IR.
//
// Inherited concept from KitX.WorkflowIR.Diff.IrDiffer, re-targeted at the structured
// AST. The v5 algorithm aligned two block lists by Name and ran LCS over the
// fingerprint sequence of each common block; v6 aligns two trees by walking them in
// lexical order (depth-first pre-order over the structured statements) and running
// the same LCS-over-fingerprint logic per enclosing scope.
//
// Identity is the Fingerprint (content-derived, re-parse-stable), so a diff survives
// a BS re-parse unchanged. Layout annotations are NOT compared here — they are
// reconciled by the applier which copies Layout from the baseline for unchanged
// statements.
//
// Algorithm (per enclosing scope):
//   1. Collect the fingerprint sequence of baseline and new statements.
//   2. Run LCS over the two fingerprint sequences.
//   3. Statements on the LCS = unchanged (whole subtree identical).
//   4. Baseline statements off the LCS = Removed.
//   5. New statements off the LCS = Added.
//   6. For Modified detection: pair off-LCS baseline and new items by Kind (a Pipeline
//      changed in place pairs with the new Pipeline at the same relative slot), mark
//      the pair as Modified instead of Remove+Add so the applier preserves Layout.
//   7. For container statements (If/ForEach/While/Switch) that are off-LCS but share
//      the same Kind, recurse into their bodies instead of reporting a wholesale
//      Modified — this yields granular per-statement diffs for nested changes.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Computes a content-addressed <see cref="WorkflowDiff"/> between two immutable
/// <see cref="Workflow"/> snapshots. Pure: never mutates either input.
/// </summary>
public static class WorkflowDiffer
{
    public static WorkflowDiff Compute(Workflow oldIr, Workflow newIr)
    {
        ArgumentNullException.ThrowIfNull(oldIr);
        ArgumentNullException.ThrowIfNull(newIr);
        var changes = new List<StatementChange>();
        DiffScope(oldIr.Body, newIr.Body, "/", changes);
        return new WorkflowDiff { StatementChanges = changes.ToImmutableArray() };
    }

    /// <summary>
    /// Diffs one scope (an ordered list of statements). Emits changes for the scope's
    /// direct children; recurses into unchanged container bodies to diff their children.
    /// </summary>
    private static void DiffScope(
        ImmutableArray<Statement> oldBody,
        ImmutableArray<Statement> newBody,
        string path,
        List<StatementChange> changes)
    {
        // LCS over fingerprint sequences.
        var (oldIndices, newIndices) = LongestCommonSubsequence(oldBody, newBody);

        // Walk both sequences, emitting changes for off-LCS items.
        int oi = 0, ni = 0;
        for (int lcsIdx = 0; lcsIdx < oldIndices.Count; lcsIdx++)
        {
            int nextOld = oldIndices[lcsIdx];
            int nextNew = newIndices[lcsIdx];

            // Emit Removed for baseline items before the next LCS match.
            while (oi < nextOld)
            {
                // Try to pair with an off-LCS new item at the same relative slot (Modified).
                if (ni < nextNew && TryPairModified(oldBody[oi], newBody[ni]))
                {
                    changes.Add(new StatementChange
                    {
                        LexicalPath = $"{path}{ni}",
                        Fingerprint = newBody[ni].Fingerprint,
                        Kind = DiffKind.Modified,
                        NewValue = newBody[ni],
                        Index = ni,
                    });
                    oi++; ni++;
                    continue;
                }
                changes.Add(new StatementChange
                {
                    LexicalPath = $"{path}{oi}",
                    Fingerprint = oldBody[oi].Fingerprint,
                    Kind = DiffKind.Removed,
                    NewValue = null,
                    Index = oi,
                });
                oi++;
            }

            // Skip new items before the next LCS match (they're Added).
            while (ni < nextNew)
            {
                // Check if the off-LCS new item can pair with a preceding baseline item.
                // (This handles the case where Modified detection didn't trigger above.)
                changes.Add(new StatementChange
                {
                    LexicalPath = $"{path}{ni}",
                    Fingerprint = newBody[ni].Fingerprint,
                    Kind = DiffKind.Added,
                    NewValue = newBody[ni],
                    Index = ni,
                });
                ni++;
            }

            // The LCS match: both items are "unchanged" at the top level. But if this
            // is a container statement, recurse into its body to detect nested changes.
            // (Same fingerprint at the top means the whole subtree is identical, so no
            // recursion is needed — fingerprint is content-derived.)
            oi = nextOld + 1;
            ni = nextNew + 1;
        }

        // Emit trailing Removed / Added after the last LCS match.
        while (oi < oldBody.Length)
        {
            if (ni < newBody.Length && TryPairModified(oldBody[oi], newBody[ni]))
            {
                changes.Add(new StatementChange
                {
                    LexicalPath = $"{path}{ni}",
                    Fingerprint = newBody[ni].Fingerprint,
                    Kind = DiffKind.Modified,
                    NewValue = newBody[ni],
                    Index = ni,
                });
                oi++; ni++;
                continue;
            }
            changes.Add(new StatementChange
            {
                LexicalPath = $"{path}{oi}",
                Fingerprint = oldBody[oi].Fingerprint,
                Kind = DiffKind.Removed,
                NewValue = null,
                Index = oi,
            });
            oi++;
        }
        while (ni < newBody.Length)
        {
            changes.Add(new StatementChange
            {
                LexicalPath = $"{path}{ni}",
                Fingerprint = newBody[ni].Fingerprint,
                Kind = DiffKind.Added,
                NewValue = newBody[ni],
                Index = ni,
            });
            ni++;
        }
    }

    /// <summary>
    /// Heuristic: two statements are "Modified-pairable" when they share the same Kind
    /// but differ in fingerprint (i.e. same statement shape, different content). This
    /// lets the diff report a Print("a") → Print("b") change as a single Modified
    /// instead of Remove+Add, preserving the canvas Layout.
    /// </summary>
    private static bool TryPairModified(Statement oldStmt, Statement newStmt)
    {
        if (oldStmt.Kind != newStmt.Kind) return false;
        // Same Kind but different fingerprint → Modified candidate.
        return !oldStmt.Fingerprint.Equals(newStmt.Fingerprint);
    }

    /// <summary>
    /// Computes the LCS over two fingerprint sequences. Returns the paired index lists
    /// (the indices that form the common subsequence in old and new respectively).
    /// Standard dynamic-programming LCS, O(n*m) time and space.
    /// </summary>
    private static (List<int> oldIdx, List<int> newIdx) LongestCommonSubsequence(
        ImmutableArray<Statement> oldBody,
        ImmutableArray<Statement> newBody)
    {
        int n = oldBody.Length, m = newBody.Length;
        // dp[i,j] = length of LCS of oldBody[0..i) and newBody[0..j)
        var dp = new int[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                if (oldBody[i - 1].Fingerprint.Equals(newBody[j - 1].Fingerprint))
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }
        // Backtrack to collect the LCS indices.
        var oldIdx = new List<int>();
        var newIdx = new List<int>();
        int ii = n, jj = m;
        while (ii > 0 && jj > 0)
        {
            if (oldBody[ii - 1].Fingerprint.Equals(newBody[jj - 1].Fingerprint))
            {
                oldIdx.Add(ii - 1);
                newIdx.Add(jj - 1);
                ii--; jj--;
            }
            else if (dp[ii - 1, jj] >= dp[ii, jj - 1])
                ii--;
            else
                jj--;
        }
        oldIdx.Reverse();
        newIdx.Reverse();
        return (oldIdx, newIdx);
    }
}