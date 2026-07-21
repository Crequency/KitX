namespace KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// ForEachStatement — structured collection iteration (discussion notes §3.3 #4).
//
// This is the answer to the ForLoop retirement RFC (see ForLoop-Retirement-And-Each-Design.md):
// the v5 ForLoop(counter, "LoopBody", "LoopEnd") control-flow terminator with its
// implicit counter var and manual Goto("LoopBody") re-entry is replaced by a
// structured <c>forEach list as item { body }</c>. The iteration variable is a
// *real* input of the body (not a string-var-name injection); the back edge is
// implicit (no Goto); and the "statements that should run once" no longer live
// inside the loop body, so the v5 footgun ("one-shot statement re-executed by
// the Goto loop-back") cannot occur.
//
// MVP scope (discussion notes §8.2) lists forEach + Range as the iteration story.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A forEach statement: <c>forEach &lt;source&gt; as &lt;itemName&gt; { body }</c>.
/// The body is executed once per element of <see cref="Source"/>, with the element
/// bound to <see cref="ItemName"/> in the body's lexical scope.
/// </summary>
public sealed record ForEachStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <summary>The collection-producing expression (raw BS text form, refined during implementation).</summary>
    public required string Source { get; init; }

    /// <summary>The name of the element binding inside the body.</summary>
    public required string ItemName { get; init; }

    /// <summary>The body executed per element.</summary>
    public required ImmutableArray<Statement> Body { get; init; } = [];

    public bool Equals(ForEachStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (Comment != other.Comment || SourceLine != other.SourceLine) return false;
        if (Source != other.Source) return false;
        if (ItemName != other.ItemName) return false;
        if (!Body.SequenceEqual(other.Body)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(Comment);
        hash.Add(SourceLine);
        hash.Add(Source);
        hash.Add(ItemName);
        foreach (var s in Body) hash.Add(s);
        return hash.ToHashCode();
    }
}
