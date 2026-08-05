namespace KitX.WorkflowV6.Diff;

using KitX.Core.Contract.Workflow;
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
// a KS re-parse unchanged. Layout annotations are NOT compared here — they are
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
        DiffBody(oldIr.Body, newIr.Body, "/", changes);
        var declarationChanges = DiffDeclarations(oldIr, newIr);
        return new WorkflowDiff
        {
            StatementChanges = changes.ToImmutableArray(),
            DeclarationChanges = declarationChanges,
        };
    }

    // ── Declaration sections (Constants / GlobalVars / HelperFunctions) ─────

    /// <summary>
    /// Diffs the declaration sections. Constants and global vars are name-keyed
    /// dictionaries — name is the stable identity, so changes align by name. Helper
    /// functions are Contract-typed classes (reference equality), so they are aligned
    /// by Name and compared field-wise.
    /// </summary>
    private static ImmutableArray<DeclarationChange> DiffDeclarations(
        Workflow oldIr, Workflow newIr)
    {
        var changes = new List<DeclarationChange>();
        DiffNameKeyed(oldIr.Constants, newIr.Constants, DeclarationSection.Constants, changes);
        DiffNameKeyed(oldIr.GlobalVars, newIr.GlobalVars, DeclarationSection.GlobalVars, changes);
        DiffHelperFunctions(oldIr.HelperFunctions, newIr.HelperFunctions, changes);
        return changes.ToImmutableArray();
    }

    private static void DiffNameKeyed<T>(
        ImmutableDictionary<string, T> oldDict,
        ImmutableDictionary<string, T> newDict,
        DeclarationSection section,
        List<DeclarationChange> changes)
        where T : class
    {
        foreach (var (name, oldValue) in oldDict)
        {
            if (!newDict.TryGetValue(name, out var newValue))
            {
                changes.Add(new DeclarationChange
                {
                    Section = section,
                    Name = name,
                    Kind = DiffKind.Removed,
                });
            }
            else if (!oldValue.Equals(newValue))
            {
                changes.Add(new DeclarationChange
                {
                    Section = section,
                    Name = name,
                    Kind = DiffKind.Modified,
                    NewValue = newValue,
                });
            }
        }
        foreach (var (name, newValue) in newDict)
        {
            if (!oldDict.ContainsKey(name))
            {
                changes.Add(new DeclarationChange
                {
                    Section = section,
                    Name = name,
                    Kind = DiffKind.Added,
                    NewValue = newValue,
                });
            }
        }
    }

    private static void DiffHelperFunctions(
        ImmutableArray<HelperFunction> oldHelpers,
        ImmutableArray<HelperFunction> newHelpers,
        List<DeclarationChange> changes)
    {
        var oldByName = oldHelpers
            .Where(h => !string.IsNullOrEmpty(h.Name))
            .ToDictionary(h => h.Name!, StringComparer.Ordinal);
        var newByName = newHelpers
            .Where(h => !string.IsNullOrEmpty(h.Name))
            .ToDictionary(h => h.Name!, StringComparer.Ordinal);

        foreach (var (name, oldHelper) in oldByName)
        {
            if (!newByName.TryGetValue(name, out var newHelper))
            {
                changes.Add(new DeclarationChange
                {
                    Section = DeclarationSection.HelperFunctions,
                    Name = name,
                    Kind = DiffKind.Removed,
                });
            }
            else if (!HelperFunctionsEqual(oldHelper, newHelper))
            {
                changes.Add(new DeclarationChange
                {
                    Section = DeclarationSection.HelperFunctions,
                    Name = name,
                    Kind = DiffKind.Modified,
                    NewValue = newHelper,
                });
            }
        }
        foreach (var (name, newHelper) in newByName)
        {
            if (!oldByName.ContainsKey(name))
            {
                changes.Add(new DeclarationChange
                {
                    Section = DeclarationSection.HelperFunctions,
                    Name = name,
                    Kind = DiffKind.Added,
                    NewValue = newHelper,
                });
            }
        }
    }

    /// <summary>
    /// Field-wise comparison for <see cref="HelperFunction"/> (a Contract class without
    /// value semantics — reference equality would report every re-parse as Modified).
    /// </summary>
    private static bool HelperFunctionsEqual(HelperFunction a, HelperFunction b)
    {
        if (a.Name != b.Name || a.ReturnType != b.ReturnType || a.Code != b.Code) return false;
        if (a.Parameters.Count != b.Parameters.Count) return false;
        for (int i = 0; i < a.Parameters.Count; i++)
        {
            if (a.Parameters[i].Name != b.Parameters[i].Name) return false;
            if (a.Parameters[i].Type != b.Parameters[i].Type) return false;
        }
        return true;
    }

    /// <summary>
    /// Diffs one scope (an ordered list of statements). Emits changes for the scope's
    /// direct children; recurses into unchanged container bodies to diff their children.
    /// </summary>
    private static void DiffBody(
        ImmutableArray<Statement> oldBody,
        ImmutableArray<Statement> newBody,
        string path,
        List<StatementChange> changes)
    {
        var (oldIndices, newIndices) = LongestCommonSubsequence(oldBody, newBody);

        int oi = 0, ni = 0;
        for (int lcsIdx = 0; lcsIdx < oldIndices.Count; lcsIdx++)
        {
            int nextOld = oldIndices[lcsIdx];
            int nextNew = newIndices[lcsIdx];

            while (oi < nextOld)
            {
                if (ni < nextNew && TryPairModified(oldBody[oi], newBody[ni]))
                {
                    if (IsContainerStatement(oldBody[oi]) && IsContainerStatement(newBody[ni]))
                        EmitContainerDiff(oldBody[oi], newBody[ni], path, ni, oi, changes);
                    else
                        EmitModified(newBody[ni], path, ni, oi, changes);
                    oi++; ni++;
                    continue;
                }
                EmitRemoved(oldBody[oi], path, oi, changes);
                oi++;
            }

            while (ni < nextNew)
            {
                EmitAdded(newBody[ni], path, ni, changes);
                ni++;
            }

            oi = nextOld + 1;
            ni = nextNew + 1;
        }

        while (oi < oldBody.Length)
        {
            if (ni < newBody.Length && TryPairModified(oldBody[oi], newBody[ni]))
            {
                if (IsContainerStatement(oldBody[oi]) && IsContainerStatement(newBody[ni]))
                    EmitContainerDiff(oldBody[oi], newBody[ni], path, ni, oi, changes);
                else
                    EmitModified(newBody[ni], path, ni, oi, changes);
                oi++; ni++;
                continue;
            }
            EmitRemoved(oldBody[oi], path, oi, changes);
            oi++;
        }
        while (ni < newBody.Length)
        {
            EmitAdded(newBody[ni], path, ni, changes);
            ni++;
        }
    }

    private static void EmitModified(
        Statement newStmt, string path, int idx, int oldIdx, List<StatementChange> changes)
    {
        changes.Add(new StatementChange
        {
            LexicalPath = ChildPath(path, idx),
            Fingerprint = newStmt.Fingerprint,
            Kind = DiffKind.Modified,
            NewValue = newStmt,
            Index = idx,
            OldIndex = oldIdx,
        });
    }

    private static void EmitRemoved(Statement oldStmt, string path, int idx, List<StatementChange> changes)
    {
        changes.Add(new StatementChange
        {
            LexicalPath = ChildPath(path, idx),
            Fingerprint = oldStmt.Fingerprint,
            Kind = DiffKind.Removed,
            NewValue = null,
            Index = idx,
            OldIndex = idx,
        });
    }

    private static void EmitAdded(Statement newStmt, string path, int idx, List<StatementChange> changes)
    {
        changes.Add(new StatementChange
        {
            LexicalPath = ChildPath(path, idx),
            Fingerprint = newStmt.Fingerprint,
            Kind = DiffKind.Added,
            NewValue = newStmt,
            Index = idx,
        });
    }

    private static void EmitContainerDiff(
        Statement oldStmt, Statement newStmt, string path, int idx, int oldIdx, List<StatementChange> changes)
    {
        // Emit a whole-container Modified to capture non-body field changes
        // (e.g. if.Condition, forEach.Source/ItemName, switch.Selector).
        // Sub-body diffs use deeper paths (e.g. /0/then/0), so they coexist without conflict.
        changes.Add(new StatementChange
        {
            LexicalPath = ChildPath(path, idx),
            Fingerprint = newStmt.Fingerprint,
            Kind = DiffKind.Modified,
            NewValue = newStmt,
            Index = idx,
            OldIndex = oldIdx,
        });
        // Also recurse into sub-bodies for granular per-statement diffs.
        foreach (var c in DiffContainerBodies(oldStmt, newStmt, ChildPath(path, idx)))
            changes.Add(c);
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

    private static string ChildPath(string prefix, int index) =>
        prefix == "/" ? $"/{index}" : $"{prefix}/{index}";

    private static bool IsContainerStatement(Statement stmt) =>
        stmt is IfStatement or ForEachStatement or WhileStatement or SwitchStatement;

    private static IEnumerable<StatementChange> DiffContainerBodies(
        Statement oldStmt, Statement newStmt, string basePath)
    {
        return (oldStmt, newStmt) switch
        {
            (IfStatement o, IfStatement n) => DiffIfBodies(o, n, basePath),
            (ForEachStatement o, ForEachStatement n) => DiffBodyEnumerable(o.Body, n.Body, $"{basePath}/body"),
            (WhileStatement o, WhileStatement n) => DiffBodyEnumerable(o.Body, n.Body, $"{basePath}/body"),
            (SwitchStatement o, SwitchStatement n) => DiffSwitchArms(o, n, basePath),
            _ => []
        };
    }

    private static IEnumerable<StatementChange> DiffIfBodies(
        IfStatement o, IfStatement n, string basePath)
    {
        foreach (var c in DiffBodyEnumerable(o.ThenBody, n.ThenBody, $"{basePath}/then"))
            yield return c;
        foreach (var c in DiffBodyEnumerable(o.ElseBody, n.ElseBody, $"{basePath}/else"))
            yield return c;
    }

    private static IEnumerable<StatementChange> DiffSwitchArms(
        SwitchStatement o, SwitchStatement n, string basePath)
    {
        int maxArms = Math.Max(o.Arms.Length, n.Arms.Length);
        for (int i = 0; i < maxArms; i++)
        {
            var oldArm = i < o.Arms.Length ? o.Arms[i] : [];
            var newArm = i < n.Arms.Length ? n.Arms[i] : [];
            foreach (var c in DiffBodyEnumerable(oldArm, newArm, $"{basePath}/arm/{i}"))
                yield return c;
        }
        foreach (var c in DiffBodyEnumerable(o.Default, n.Default, $"{basePath}/default"))
            yield return c;
    }

    private static IEnumerable<StatementChange> DiffBodyEnumerable(
        ImmutableArray<Statement> oldBody,
        ImmutableArray<Statement> newBody,
        string basePath)
    {
        var changes = new List<StatementChange>();
        DiffBody(oldBody, newBody, basePath, changes);
        return changes;
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