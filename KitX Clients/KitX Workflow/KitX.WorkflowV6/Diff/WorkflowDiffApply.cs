namespace KitX.WorkflowV6.Diff;

using System.Linq;
using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowDiffApply — the pure applier that turns a WorkflowDiff + baseline into a
// new immutable Workflow.
//
// Inherited concept from KitX.WorkflowIR.Diff.IrDiffApply, re-targeted at the
// structured IR. For each scope (list of statements), the applier REBUILDS the
// target list position by position instead of applying remove-then-insert in some
// order:
//   • a target slot occupied by an Added/Modified change → its NewValue;
//   • any other target slot → the next baseline statement that was neither
//     Removed nor Modified (in baseline order).
// Removed carries the baseline index (OldIndex), Added the target index (Index),
// Modified the (old → new) slot pair — so a pure reorder like [A,B] → [B,A]
// applies correctly, which no fixed remove/insert order can do.
//
// Declaration sections (Constants / GlobalVars / HelperFunctions) are applied
// name-keyed from the DeclarationChanges list.
//
// Layout annotation reconciliation: unchanged statements keep their baseline
// instance (Layout intact); Modified statements get Layout copied from their
// baseline counterpart. This is the central UX requirement (discussion notes §7):
// editing one Print statement must not disturb the canvas positions of other nodes.
//
// Pure: never mutates the baseline; produces a fresh Workflow.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Applies a <see cref="WorkflowDiff"/> to a baseline <see cref="Workflow"/>, producing
/// a new immutable Workflow. Pure: the baseline is never mutated.
/// </summary>
public static class WorkflowDiffApply
{
    /// <summary>
    /// Applies <paramref name="diff"/> to <paramref name="baseline"/> and returns the
    /// resulting Workflow. When <paramref name="diff"/> is empty, returns the baseline
    /// unchanged.
    /// </summary>
    public static Workflow Apply(Workflow baseline, WorkflowDiff diff)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(diff);
        if (diff.IsEmpty) return baseline;

        // Group changes by their top-level scope path (the lexical path up to the last
        // '/' separator). Only top-level changes are applied by name; nested-scope
        // changes arrive as container replacements (the container statement is replaced
        // wholesale with its new body) rather than recursive per-statement edits.
        var newBody = ApplyChangesToScope(baseline.Body, diff.StatementChanges, "/");
        return ApplyDeclarationChanges(baseline with { Body = newBody }, diff.DeclarationChanges);
    }

    /// <summary>
    /// Rebuilds one scope (an ordered list of statements) as the target list, using
    /// the baseline as the source of unchanged statements. See the file header for
    /// why remove-then-insert ordering cannot represent pure reorders.
    /// </summary>
    private static ImmutableArray<Statement> ApplyChangesToScope(
        ImmutableArray<Statement> body,
        ImmutableArray<StatementChange> changes,
        string scopePath)
    {
        // Filter changes that belong to this scope (LexicalPath starts with scopePath).
        // For top-level ("/"), a change at "/3" has LexicalPath "3" (no leading slash
        // after the scope). This is a simplification for Phase 5 MVP.
        var scopeChanges = changes
            .Where(c => IsInScope(c.LexicalPath, scopePath))
            // Whole-container Modified (emitted by EmitContainerDiff for every changed
            // container) covers nested changes — the container subtree is fully replaced.
            // Fine-grained sub-body diffs (deeper paths) are naturally skipped by the
            // IsDirectChild filter; if future Apply needs them, recurse here.
            .Where(c => IsDirectChild(c.LexicalPath, scopePath))
            .ToList();
        if (scopeChanges.Count == 0) return body;

        var removedIndices = new HashSet<int>();
        var modifiedAt = new Dictionary<int, (int OldIndex, Statement NewValue)>();
        var addedAt = new List<(int Index, Statement NewValue)>();

        foreach (var c in scopeChanges)
        {
            switch (c.Kind)
            {
                case DiffKind.Removed:
                    var removedIdx = c.OldIndex ?? c.Index ?? ResolveScopeIndex(c.LexicalPath, scopePath);
                    if (removedIdx >= 0 && removedIdx < body.Length)
                        removedIndices.Add(removedIdx);
                    break;
                case DiffKind.Modified when c.NewValue is not null:
                    var targetIdx = c.Index ?? ResolveScopeIndex(c.LexicalPath, scopePath);
                    var oldIdx = c.OldIndex ?? targetIdx;
                    if (targetIdx >= 0)
                        modifiedAt[targetIdx] = (oldIdx, c.NewValue);
                    break;
                case DiffKind.Added when c.NewValue is not null:
                    addedAt.Add((c.Index ?? body.Length, c.NewValue));
                    break;
            }
        }
        if (removedIndices.Count == 0 && modifiedAt.Count == 0 && addedAt.Count == 0)
            return body;

        // Target length: baseline minus removed plus added (Modified replaces in place).
        int targetLength = body.Length - removedIndices.Count + addedAt.Count;

        // Drop Modified changes whose target slot is out of range (invalid diff input;
        // the baseline statement then stays in place). 
        var validModified = new Dictionary<int, (int OldIndex, Statement NewValue)>();
        foreach (var (idx, pair) in modifiedAt)
            if (idx >= 0 && idx < targetLength)
                validModified[idx] = pair;
        modifiedAt = validModified;

        var modifiedOldIndices = new HashSet<int>(modifiedAt.Values.Select(p => p.OldIndex));

        // Added slots: clamp out-of-range target indices (e.g. a caller-supplied
        // Index beyond the end means "append at the end").
        var addedByIndex = new Dictionary<int, Statement>();
        int maxSlot = Math.Max(0, targetLength - 1);
        foreach (var (idx, stmt) in addedAt.OrderBy(a => a.Index))
            addedByIndex[Math.Clamp(idx, 0, maxSlot)] = stmt;

        // Baseline statements kept verbatim: everything neither Removed nor Modified.
        var keptOldIndices = new List<int>();
        for (int i = 0; i < body.Length; i++)
        {
            if (removedIndices.Contains(i)) continue;
            if (modifiedOldIndices.Contains(i)) continue;
            keptOldIndices.Add(i);
        }

        // Rebuild the target list slot by slot.
        var result = new List<Statement>(targetLength);
        int keptCursor = 0;
        for (int slot = 0; slot < targetLength; slot++)
        {
            if (addedByIndex.TryGetValue(slot, out var addedStmt))
            {
                result.Add(addedStmt);
            }
            else if (modifiedAt.TryGetValue(slot, out var mod))
            {
                var srcIdx = mod.OldIndex >= 0 && mod.OldIndex < body.Length ? mod.OldIndex : slot;
                result.Add(CopyLayoutFrom(body[srcIdx], mod.NewValue));
            }
            else if (keptCursor < keptOldIndices.Count)
            {
                result.Add(body[keptOldIndices[keptCursor++]]);
            }
        }
        return result.ToImmutableArray();
    }

    /// <summary>
    /// Resolves the ordinal index of a change within its scope, given the change's
    /// lexical path and the scope's path. For the MVP, the lexical path is just the
    /// ordinal (e.g. "3" for the 4th statement at top level).
    /// </summary>
    private static int ResolveScopeIndex(string lexicalPath, string scopePath)
    {
        // Strip the scope prefix; the remainder is the ordinal.
        var rest = lexicalPath;
        if (scopePath != "/" && rest.StartsWith(scopePath))
            rest = rest[scopePath.Length..];
        rest = rest.Trim('/');
        // For nested paths like "/0/body/3", take the last segment.
        var lastSlash = rest.LastIndexOf('/');
        if (lastSlash >= 0) rest = rest[(lastSlash + 1)..];
        return int.TryParse(rest, out var idx) ? idx : -1;
    }

    /// <summary>
    /// Determines whether a lexical path belongs to the given scope. A change is in scope
    /// when its path equals scopePath or starts with scopePath + "/". The root scope "/"
    /// matches every path starting with "/".
    /// </summary>
    private static bool IsInScope(string lexicalPath, string scopePath)
    {
        if (scopePath == "/") return lexicalPath.StartsWith("/");
        return lexicalPath == scopePath || lexicalPath.StartsWith(scopePath + "/");
    }

    /// <summary>
    /// Returns true when lexicalPath is a direct child of scopePath (i.e. exactly one
    /// path segment deeper). Nested-scope changes are applied as container replacements
    /// (the parent statement is replaced wholesale with its new body), so a change must
    /// land exactly at its own scope level.
    /// </summary>
    private static bool IsDirectChild(string lexicalPath, string scopePath)
    {
        if (scopePath == "/")
            return lexicalPath.Count(c => c == '/') == 1;
        if (!lexicalPath.StartsWith(scopePath + "/")) return false;
        return !lexicalPath[(scopePath.Length + 1)..].Contains('/');
    }

    /// <summary>
    /// Copies Layout annotations from <paramref name="baseline"/> to <paramref name="target"/>,
    /// preserving canvas positions when a statement is Modified in place. Only Layout
    /// annotations are copied (DebugHighlight etc. are not — they're runtime state).
    /// 零生产者（生产代码无 Layout annotation 产出），T5 位置持久化立项预留——勿删勿改。
    /// </summary>
    private static Statement CopyLayoutFrom(Statement baseline, Statement target)
    {
        var layoutAnns = baseline.Annotations
            .Where(a => a.Kind == "Layout")
            .ToImmutableArray();
        if (layoutAnns.IsEmpty) return target;
        return target with { Annotations = layoutAnns };
    }

    /// <summary>
    /// Applies declaration-section changes to the workflow (Constants / GlobalVars are
    /// name-keyed dictionaries; HelperFunctions are aligned by name in list order).
    /// </summary>
    private static Workflow ApplyDeclarationChanges(
        Workflow wf, ImmutableArray<DeclarationChange> changes)
    {
        if (changes.IsEmpty) return wf;
        var constants = wf.Constants;
        var globalVars = wf.GlobalVars;
        var helperList = wf.HelperFunctions.ToList();

        foreach (var c in changes)
        {
            switch (c.Section)
            {
                case DeclarationSection.Constants:
                    constants = ApplyToNameKeyedDict(constants, c);
                    break;
                case DeclarationSection.GlobalVars:
                    globalVars = ApplyToNameKeyedDict(globalVars, c);
                    break;
                case DeclarationSection.HelperFunctions:
                    helperList = ApplyToHelperList(helperList, c);
                    break;
            }
        }

        return wf with
        {
            Constants = constants,
            GlobalVars = globalVars,
            HelperFunctions = helperList.ToImmutableArray(),
        };
    }

    private static ImmutableDictionary<string, T> ApplyToNameKeyedDict<T>(
        ImmutableDictionary<string, T> dict, DeclarationChange c)
        where T : class
    {
        switch (c.Kind)
        {
            case DiffKind.Removed:
                return dict.Remove(c.Name);
            case DiffKind.Added:
            case DiffKind.Modified:
                return c.NewValue is T value ? dict.SetItem(c.Name, value) : dict;
            default:
                return dict;
        }
    }

    private static List<HelperFunction> ApplyToHelperList(
        List<HelperFunction> list, DeclarationChange c)
    {
        var idx = list.FindIndex(h => h.Name == c.Name);
        switch (c.Kind)
        {
            case DiffKind.Removed:
                if (idx >= 0) list.RemoveAt(idx);
                break;
            case DiffKind.Added:
            case DiffKind.Modified:
                if (c.NewValue is HelperFunction value)
                {
                    if (idx >= 0) list[idx] = value;
                    else list.Add(value);
                }
                break;
        }
        return list;
    }
}