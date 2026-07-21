namespace KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// WhileStatement — structured conditional loop (discussion notes §3.3 #5).
//
// Carried in the architecture skeleton because the discussion notes list it among
// the nine primitives. Whether MVP includes it is open (discussion notes §10.1:
// "while 是否需要"). The shape is reserved here so the implementation phase has a
// concrete type to either fill in or drop.
//
// Unlike forEach, while carries a dynamic condition and no element binding. Break
// and Continue escape the body; see <see cref="BreakStatement"/> and
// <see cref="ContinueStatement"/>.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A while statement: <c>while &lt;condition&gt; { body }</c>. The body repeats while
/// <see cref="Condition"/> evaluates true.
/// </summary>
public sealed record WhileStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <summary>The loop-continuation condition (raw BS text form, refined during implementation).</summary>
    public required string Condition { get; init; }

    /// <summary>The body executed while <see cref="Condition"/> holds.</summary>
    public required ImmutableArray<Statement> Body { get; init; } = [];

    public bool Equals(WhileStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (Comment != other.Comment || SourceLine != other.SourceLine) return false;
        if (Condition != other.Condition) return false;
        if (!Body.SequenceEqual(other.Body)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(Comment);
        hash.Add(SourceLine);
        hash.Add(Condition);
        foreach (var s in Body) hash.Add(s);
        return hash.ToHashCode();
    }
}
