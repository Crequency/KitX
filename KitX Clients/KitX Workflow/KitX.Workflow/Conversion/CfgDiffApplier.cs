using KitX.Workflow.CFG;

namespace KitX.Workflow.Conversion;

/// <summary>
/// Applies a <see cref="CfgDiff"/> to a live <see cref="ControlFlowGraph"/>,
/// mutating blocks, statements, and successors in place.
/// </summary>
public class CfgDiffApplier : ICfgDiffApplier
{
    public void Apply(CfgDiff diff, ControlFlowGraph liveCfg)
    {
        // ── Block-level changes ──
        foreach (var bc in diff.BlocksRemoved)
        {
            var block = liveCfg.Blocks.FirstOrDefault(b => b.Name == bc.Name);
            if (block == null) continue;

            // Clean up Successors referencing the removed block from other blocks
            foreach (var other in liveCfg.Blocks)
            {
                other.Successors.RemoveAll(e => e.ToBlockName == bc.Name);
                foreach (var stmt in other.Statements)
                {
                    if (stmt == null) continue;
                    for (int a = 0; a < stmt.Arms.Count; a++)
                    {
                        if (stmt.Arms[a].TargetBlockName == bc.Name)
                            stmt.Arms[a].TargetBlockName = string.Empty;
                    }
                }
            }
            liveCfg.Blocks.Remove(block);
        }

        foreach (var bc in diff.BlocksAdded)
        {
            if (bc.NewBlock == null) continue;
            if (liveCfg.Blocks.Any(b => b.Name == bc.Name)) continue;
            liveCfg.Blocks.Add(bc.NewBlock);
        }

        // ── Statement-level changes: group by block ──
        var byBlock = new Dictionary<string, List<Action<CFGBlock>>>();
        var blockNames = new HashSet<string>();

        // Removes: find and remove from block.Statements
        foreach (var sc in diff.Removed)
        {
            blockNames.Add(sc.BlockName);
            AddAction(sc.BlockName, block =>
            {
                var stmt = FindStatement(block, sc.StatementId, sc.Fingerprint);
                if (stmt != null) block.Statements.Remove(stmt);
            });
        }

        // Modifies: find and replace
        foreach (var sc in diff.Modified)
        {
            if (sc.NewStatement == null) continue;
            blockNames.Add(sc.BlockName);
            AddAction(sc.BlockName, block =>
            {
                var idx = FindStatementIndex(block, sc.StatementId, sc.Fingerprint);
                if (idx >= 0)
                {
                    sc.NewStatement.StatementId = block.Statements[idx].StatementId;
                    block.Statements[idx] = sc.NewStatement;
                }
            });
        }

        // Moves: remove from source, add to target
        var moveActions = new List<(string from, string to, StatementMove move, CFGStatement? stmt)>();
        foreach (var mv in diff.Moved)
        {
            blockNames.Add(mv.FromBlock);
            blockNames.Add(mv.ToBlock);
            AddAction(mv.FromBlock, block =>
            {
                var stmt = FindStatement(block, null, mv.Fingerprint);
                if (stmt != null)
                {
                    block.Statements.Remove(stmt);
                    moveActions.Add((mv.FromBlock, mv.ToBlock, mv, stmt));
                }
            });
        }

        // Adds: insert at specified index
        foreach (var sc in diff.Added)
        {
            if (sc.NewStatement == null) continue;
            blockNames.Add(sc.BlockName);
            AddAction(sc.BlockName, block =>
            {
                var idx = sc.Index ?? block.Statements.Count;
                if (idx > block.Statements.Count) idx = block.Statements.Count;
                block.Statements.Insert(idx, sc.NewStatement!);
            });
        }

        // Execute all per-block actions
        foreach (var (blockName, actions) in byBlock)
        {
            var block = liveCfg.Blocks.FirstOrDefault(b => b.Name == blockName);
            if (block == null) continue;
            foreach (var action in actions)
                action(block);
        }

        // Execute moved-statement re-insertions
        foreach (var (_, toBlock, mv, stmt) in moveActions)
        {
            if (stmt == null) continue;
            var block = liveCfg.Blocks.FirstOrDefault(b => b.Name == toBlock);
            if (block == null) continue;
            var idx = mv.ToIndex ?? block.Statements.Count;
            if (idx > block.Statements.Count) idx = block.Statements.Count;
            block.Statements.Insert(idx, stmt);
        }

        void AddAction(string blockName, Action<CFGBlock> action)
        {
            if (!byBlock.TryGetValue(blockName, out var list))
                byBlock[blockName] = list = [];
            list.Add(action);
        }
    }

    private static CFGStatement? FindStatement(CFGBlock block, string? statementId, string? fingerprint)
    {
        var idx = FindStatementIndex(block, statementId, fingerprint);
        return idx >= 0 ? block.Statements[idx] : null;
    }

    private static int FindStatementIndex(CFGBlock block, string? statementId, string? fingerprint)
    {
        for (int i = 0; i < block.Statements.Count; i++)
        {
            var s = block.Statements[i];
            if (MatchesIdentity(s, statementId, fingerprint)) return i;
            if (s is PipelineStatement ps && ps.Flattener != null)
            {
                foreach (var flat in ps.FlattenedStatements)
                {
                    if (MatchesIdentity(flat, statementId, fingerprint)) return i;
                }
            }
        }
        return -1;
    }

    private static bool MatchesIdentity(CFGStatement s, string? statementId, string? fingerprint)
    {
        if (statementId != null && s.StatementId == statementId) return true;
        if (fingerprint != null && (s.Fingerprint ?? s.OriginalExpression) == fingerprint) return true;
        return false;
    }
}
