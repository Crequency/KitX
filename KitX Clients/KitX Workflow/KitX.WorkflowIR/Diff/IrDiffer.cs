namespace KitX.WorkflowIR.Diff;

using KitX.WorkflowIR.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// IrDiffer — the semantic diff engine for the immutable IR.
//
// This is the greenfield successor to the legacy mutable CfgDiffer. The algorithm
// is reused nearly verbatim (A-level port); only the types change (string
// identity → IrFingerprint, mutable lists → ImmutableArray). Two layers:
//
//   1. Block-level diff — block identity = Name. Names in old but not new →
//      BlocksRemoved; names in new but not old → BlocksAdded. A rename is a
//      remove+add by design (block identity is name, so renaming IS a new block).
//
//   2. Per-block statement diff (common blocks only) — statement identity =
//      Fingerprint. Align old vs new statement sequences with LCS over the
//      fingerprint sequence; off-LCS deletes/inserts are classified:
//        • same fingerprint paired delete+insert  → Moved
//        • different fingerprint paired pair       → Modified
//        • unpaired delete                        → Removed
//        • unpaired insert                        → Added
//
//   3. Cross-block move detection (the one real enhancement over the legacy
//      CfgDiffer) — after per-block classification, a Removed statement whose
//      fingerprint appears as an Added statement in another common block is
//      re-reported as a single Moved change with ToBlock set, instead of a
//      remove+add pair. This is what lets a BP view animate a node sliding from
//      one block to another instead of vanishing and reappearing.
//
// Identity: the stable identity of a statement is its IrFingerprint (content-
// derived, re-parse-stable). Layout coordinates are NOT identity and are NOT
// compared here — they are reconciled by IrDiffApply (which copies Layout
// annotations from the old IR for unchanged statements).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Computes a content-addressed <see cref="IrDiff"/> between two immutable
/// <see cref="IrWorkflow"/> snapshots. Pure: never mutates either input.
/// </summary>
public static class IrDiffer
{
    /// <summary>
    /// Computes the semantic delta from <paramref name="oldIr"/> to
    /// <paramref name="newIr"/>. The result is keyed by <see cref="IrFingerprint"/>
    /// so it survives a BS re-parse (unlike the legacy diff, which was keyed on a
    /// random Guid StatementId and so was useless across a re-parse).
    /// </summary>
    public static IrDiff Compute(IrWorkflow oldIr, IrWorkflow newIr)
    {
        // ── Block-level diff (identity = name) ──
        var oldByBlock = oldIr.Blocks.ToDictionary(b => b.Name);
        var newByBlock = newIr.Blocks.ToDictionary(b => b.Name);
        var oldNames = oldIr.Blocks.Select(b => b.Name).ToHashSet();
        var newNames = newIr.Blocks.Select(b => b.Name).ToHashSet();

        var blockChanges = ImmutableArray.CreateBuilder<BlockChange>();

        // Removals first (so an applier can drop dead blocks before inserting new ones).
        foreach (var name in oldNames.Except(newNames))
            blockChanges.Add(new BlockChange { Name = name, Kind = BlockChangeKind.Removed });
        foreach (var name in newNames.Except(oldNames))
            blockChanges.Add(new BlockChange
            {
                Name = name,
                Kind = BlockChangeKind.Added,
                NewBlock = newByBlock[name],
            });

        // ── Per-block statement diff (common blocks only) ──
        var statementChanges = ImmutableArray.CreateBuilder<StatementChange>();

        // Track per-block removed/added so we can pair cross-block moves afterwards.
        var removedByBlock = new Dictionary<string, List<(int Index, IrStatement Stmt)>>();
        var addedByBlock = new Dictionary<string, List<(int Index, IrStatement Stmt)>>();

        foreach (var name in oldNames.Intersect(newNames))
        {
            var oldStmts = oldByBlock[name].Statements;
            var newStmts = newByBlock[name].Statements;
            var oldIds = oldStmts.Select(IdentityOf).ToList();
            var newIds = newStmts.Select(IdentityOf).ToList();

            // LCS over the fingerprint sequence: statements on the LCS stay in place.
            var (lcsOld, lcsNew) = LongestCommonSubsequenceIndices(oldIds, newIds);
            var matchedOld = lcsOld.ToHashSet();
            var matchedNew = lcsNew.ToHashSet();

            // Off-LCS deletes/inserts in positional order.
            var delIdx = Enumerable.Range(0, oldIds.Count).Where(i => !matchedOld.Contains(i)).ToList();
            var insIdx = Enumerable.Range(0, newIds.Count).Where(j => !matchedNew.Contains(j)).ToList();

            // Walk paired delete+insert slots: same identity → Move, else → Modify.
            var delConsumed = new bool[delIdx.Count];
            var insConsumed = new bool[insIdx.Count];
            int pairs = System.Math.Min(delIdx.Count, insIdx.Count);
            for (int k = 0; k < pairs; k++)
            {
                var oldIdxPos = delIdx[k];
                var newIdxPos = insIdx[k];
                var oldId = oldIds[oldIdxPos];
                var newId = newIds[newIdxPos];
                if (oldId == newId)
                {
                    statementChanges.Add(new StatementChange
                    {
                        BlockName = name,
                        Fingerprint = newId,
                        Kind = DiffKind.Moved,
                        FromIndex = oldIdxPos,
                        NewIndex = newIdxPos,
                        ToBlock = name,
                    });
                }
                else
                {
                    // Modify: report under the NEW fingerprint (the identity the result
                    // carries), but also record FromIndex (the OLD slot) so IrDiffApply
                    // can replace it positionally without a fragile fingerprint search
                    // (the fingerprint changed, so it cannot be located by it).
                    statementChanges.Add(new StatementChange
                    {
                        BlockName = name,
                        Fingerprint = newId,
                        Kind = DiffKind.Modified,
                        NewValue = newStmts[newIdxPos],
                        FromIndex = oldIdxPos,
                    });
                }
                delConsumed[k] = true;
                insConsumed[k] = true;
            }

            // Unpaired deletes → Removed (defer cross-block pairing).
            var removedHere = new List<(int, IrStatement)>();
            for (int i = 0; i < delIdx.Count; i++)
            {
                if (!delConsumed[i])
                {
                    var idx = delIdx[i];
                    removedHere.Add((idx, oldStmts[idx]));
                }
            }
            if (removedHere.Count > 0) removedByBlock[name] = removedHere;

            // Unpaired inserts → Added (defer cross-block pairing).
            var addedHere = new List<(int, IrStatement)>();
            for (int j = 0; j < insIdx.Count; j++)
            {
                if (!insConsumed[j])
                {
                    var idx = insIdx[j];
                    addedHere.Add((idx, newStmts[idx]));
                }
            }
            if (addedHere.Count > 0) addedByBlock[name] = addedHere;
        }

        // ── Cross-block move detection ──
        // A Removed fingerprint that also appears as an Added fingerprint in a
        // DIFFERENT common block is a cross-block move: report one Moved change
        // (ToBlock = destination) instead of a remove+add pair.
        var pairedRemoves = new HashSet<(string Block, int Index)>();
        var pairedAdds = new HashSet<(string Block, int Index)>();

        foreach (var (srcBlock, removed) in removedByBlock)
        {
            foreach (var (rIdx, rStmt) in removed)
            {
                var rFp = IdentityOf(rStmt);
                // Find the first Added statement with the same fingerprint in a
                // different common block that hasn't already been paired.
                foreach (var (dstBlock, added) in addedByBlock)
                {
                    if (dstBlock == srcBlock) continue;
                    for (int a = 0; a < added.Count; a++)
                    {
                        if (pairedAdds.Contains((dstBlock, a))) continue;
                        var (aIdx, aStmt) = added[a];
                        if (IdentityOf(aStmt) == rFp)
                        {
                            statementChanges.Add(new StatementChange
                            {
                                BlockName = srcBlock,
                                Fingerprint = rFp,
                                Kind = DiffKind.Moved,
                                FromIndex = rIdx,
                                NewIndex = aIdx,
                                ToBlock = dstBlock,
                                // Carry the statement value so IrDiffApply can insert it
                                // in the destination block without needing the source
                                // block's old list (the source is rebuilt independently).
                                NewValue = aStmt,
                            });
                            pairedRemoves.Add((srcBlock, rIdx));
                            pairedAdds.Add((dstBlock, a));
                            break;
                        }
                    }
                    if (pairedRemoves.Contains((srcBlock, rIdx))) break;
                }
            }
        }

        // Emit the unpaired removes/adds as plain Removed/Added changes.
        foreach (var (block, removed) in removedByBlock)
        {
            foreach (var (idx, stmt) in removed)
            {
                if (pairedRemoves.Contains((block, idx))) continue;
                statementChanges.Add(new StatementChange
                {
                    BlockName = block,
                    Fingerprint = IdentityOf(stmt),
                    Kind = DiffKind.Removed,
                });
            }
        }
        foreach (var (block, added) in addedByBlock)
        {
            for (int a = 0; a < added.Count; a++)
            {
                if (pairedAdds.Contains((block, a))) continue;
                var (idx, stmt) = added[a];
                statementChanges.Add(new StatementChange
                {
                    BlockName = block,
                    Fingerprint = IdentityOf(stmt),
                    Kind = DiffKind.Added,
                    NewValue = stmt,
                    NewIndex = idx,
                });
            }
        }

        return new IrDiff
        {
            BlockChanges = blockChanges.ToImmutable(),
            StatementChanges = statementChanges.ToImmutable(),
        };
    }

    /// <summary>
    /// The stable identity of a statement — its content-derived
    /// <see cref="IrFingerprint"/>. This is what the LCS aligns on, so two
    /// structurally identical statements match even if they were constructed at
    /// different times (the legacy random-Guid StatementId could never do this).
    /// </summary>
    internal static IrFingerprint IdentityOf(IrStatement s) => s.Fingerprint;

    /// <summary>
    /// Returns the matched index pairs of an LCS over <paramref name="a"/> and
    /// <paramref name="b"/>, as two parallel lists (old indices and new indices that
    /// form the common subsequence). Standard DP: build a backward LCS-length table,
    /// then forward-walk to emit the matching pairs.
    ///
    /// <para>Ported verbatim from the legacy CfgDiffer.LongestCommonSubsequenceIndices
    /// (only the element type changes: <c>string</c> → <c>IrFingerprint</c>).</para>
    /// </summary>
    private static (List<int> OldIdx, List<int> NewIdx) LongestCommonSubsequenceIndices(
        IReadOnlyList<IrFingerprint> a, IReadOnlyList<IrFingerprint> b)
    {
        int n = a.Count, m = b.Count;
        // dp[i,j] = length of LCS of a[i..] and b[j..]
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : System.Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var oldIdx = new List<int>();
        var newIdx = new List<int>();
        int ii = 0, jj = 0;
        while (ii < n && jj < m)
        {
            if (a[ii] == b[jj]) { oldIdx.Add(ii); newIdx.Add(jj); ii++; jj++; }
            else if (dp[ii + 1, jj] >= dp[ii, jj + 1]) ii++;
            else jj++;
        }
        return (oldIdx, newIdx);
    }
}
