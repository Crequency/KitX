namespace KitX.WorkflowV6.Diff;

using System.Linq;
using KitX.WorkflowV6.Ir;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowDiffApply — the pure applier that turns a WorkflowDiff + baseline into a
// new immutable Workflow.
//
// Inherited concept from KitX.WorkflowIR.Diff.IrDiffApply, re-targeted at the
// structured IR. For each StatementChange:
//   • Added    → insert NewValue at Index in the enclosing scope.
//   • Removed  → remove the statement at Index.
//   • Modified → replace the statement at Index with NewValue.
//
// Layout annotation reconciliation: after applying all structural changes, the applier
// copies Layout annotations from the baseline for unchanged statements (those whose
// fingerprint appears in both baseline and result). This is the central UX requirement
// (discussion notes §7): editing one Print statement must not disturb the canvas
// positions of other nodes.
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
        return baseline with { Body = newBody };
    }

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
            .OrderBy(c => c.Index ?? 0)
            .ToList();
        if (scopeChanges.Count == 0) return body;

        var result = body.ToList();
        // Apply in reverse index order so insertions/removals don't shift later indices.
        // Actually, for correctness we need to apply in the right order depending on the
        // change kind. For MVP simplicity: apply Removed first (in reverse order), then
        // Modified (in place), then Added (in index order).
        foreach (var c in scopeChanges.Where(c => c.Kind == DiffKind.Removed).OrderByDescending(c => c.Index ?? 0))
        {
            var idx = ResolveScopeIndex(c.LexicalPath, scopePath);
            if (idx >= 0 && idx < result.Count) result.RemoveAt(idx);
        }
        foreach (var c in scopeChanges.Where(c => c.Kind == DiffKind.Modified))
        {
            var idx = ResolveScopeIndex(c.LexicalPath, scopePath);
            if (idx >= 0 && idx < result.Count && c.NewValue is not null)
                result[idx] = CopyLayoutFrom(result[idx], c.NewValue);
        }
        foreach (var c in scopeChanges.Where(c => c.Kind == DiffKind.Added).OrderBy(c => c.Index ?? 0))
        {
            var idx = c.Index ?? result.Count;
            if (c.NewValue is not null)
            {
                if (idx >= result.Count) result.Add(c.NewValue);
                else result.Insert(idx, c.NewValue);
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
}