using System.Collections.Generic;
using System.Linq;
using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Semantic diff between two <see cref="ControlFlowGraph"/>s — the core mechanism for the
/// minimal-change sync feature (Blueprint-Editor-Redesign-Plan §功能A). When BS text is edited
/// and re-parsed into a new CFG, the differ computes what changed so the BP view can update
/// only the affected nodes/edges instead of rebuilding wholesale.
///
/// <para>Identity strategy (per <see cref="ICFGDiffer"/> remarks):</para>
/// <list type="bullet">
///   <item><b>Block identity</b> = block name (stable; rename = delete+insert).</item>
///   <item><b>Statement identity</b> = <see cref="CFGStatement.Fingerprint"/>
///       (function name + argument fingerprint; stable across re-parse).</item>
///   <item><b>In-block alignment</b> = LCS over the fingerprint sequence
///       (a moved statement is reported as Move, not Delete+Insert).</item>
/// </list>
/// </summary>
public class CfgDiffer : ICFGDiffer
{
    /// <inheritdoc/>
    public CfgDiff Diff(ControlFlowGraph oldCfg, ControlFlowGraph newCfg)
    {
        var oldBlocks = oldCfg.Blocks;
        var newBlocks = newCfg.Blocks;

        // ── Block-level diff (identity = name) ──
        var oldByBlock = oldBlocks.ToDictionary(b => b.Name);
        var newByBlock = newBlocks.ToDictionary(b => b.Name);
        var oldNames = oldBlocks.Select(b => b.Name).ToHashSet();
        var newNames = newBlocks.Select(b => b.Name).ToHashSet();
        var blocksRemoved = oldNames.Except(newNames)
            .Select(n => new BlockChange(n, null)).ToList();
        var blocksAdded = newNames.Except(oldNames)
            .Select(n => new BlockChange(n, newByBlock.GetValueOrDefault(n))).ToList();

        // ── Per-block statement diff (only for common blocks) ──
        var added = new List<StatementChange>();
        var removed = new List<StatementChange>();
        var modified = new List<StatementChange>();
        var moved = new List<StatementMove>();

        foreach (var name in oldNames.Intersect(newNames))
        {
            var oldStmts = oldByBlock[name].GetEffectiveStatements().ToList();
            var newStmts = newByBlock[name].GetEffectiveStatements().ToList();
            var oldIds = oldStmts.Select(IdentityOf).ToList();
            var newIds = newStmts.Select(IdentityOf).ToList();

            // LCS over the identity sequence: statements on the LCS stay in place (in order).
            var (lcsOld, lcsNew) = LongestCommonSubsequenceIndices(oldIds, newIds);
            var matchedOld = lcsOld.ToHashSet();
            var matchedNew = lcsNew.ToHashSet();

            // Off-LCS deletes/inserts (positional index lists).
            var delIdx = Enumerable.Range(0, oldIds.Count).Where(i => !matchedOld.Contains(i)).ToList();
            var insIdx = Enumerable.Range(0, newIds.Count).Where(j => !matchedNew.Contains(j)).ToList();

            // Classify each off-LCS delete+insert pair, walking both lists in positional order:
            //  - same identity  → Move (consumes both the delete and the insert)
            //  - diff identity  → Modify (consumes both; reported once with the new identity)
            // Unpaired deletes (no insert at this slot) → Remove.
            // Unpaired inserts (no delete at this slot)  → Add.
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
                    moved.Add(new StatementMove(oldId, name, name, oldIdxPos, newIdxPos));
                }
                else
                {
                    modified.Add(new StatementChange(name, newId,
                        StatementId: newStmts[newIdxPos].StatementId,
                        NewStatement: newStmts[newIdxPos]));
                }
                delConsumed[k] = true;
                insConsumed[k] = true;
            }

            // Remaining deletes → Remove.
            for (int i = 0; i < delIdx.Count; i++)
                if (!delConsumed[i])
                {
                    var idx = delIdx[i];
                    removed.Add(new StatementChange(name, oldIds[idx], oldStmts[idx].StatementId));
                }
            // Remaining inserts → Add.
            for (int j = 0; j < insIdx.Count; j++)
                if (!insConsumed[j])
                {
                    var idx = insIdx[j];
                    added.Add(new StatementChange(name, newIds[idx],
                        StatementId: newStmts[idx].StatementId,
                        NewStatement: newStmts[idx],
                        Index: idx));
                }
        }

        return new CfgDiff
        {
            Added = added,
            Removed = removed,
            Modified = modified,
            Moved = moved,
            BlocksAdded = blocksAdded,
            BlocksRemoved = blocksRemoved,
        };
    }

    /// <summary>The stable identity of a statement — matches what the tests compare against
    /// (<c>s.Fingerprint ?? s.OriginalExpression</c>).</summary>
    private static string IdentityOf(CFGStatement s) => s.Fingerprint ?? s.OriginalExpression;

    /// <summary>
    /// Returns the matched index pairs of an LCS over <paramref name="a"/> and <paramref name="b"/>,
    /// as two parallel lists (old indices and new indices that form the common subsequence).
    /// </summary>
    private static (List<int> OldIdx, List<int> NewIdx) LongestCommonSubsequenceIndices(
        List<string> a, List<string> b)
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