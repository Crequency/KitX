namespace KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// IfStatement — the structured if/else primitive (BlockScript discussion notes §3.3 #2).
//
// Replaces the v5 Branch(cond, "True", "False") control-flow terminator + the named
// "True"/"False" blocks it transferred to. In v6 the branches are *children* of the
// IfStatement, lexically nested. There is no block-name addressing, no Goto, no
// trampoline case. The 1:1 BP↔BS↔IR mapping holds: an IfStatement is one Branch node
// whose True/False output pins each connect to the subgraph for the corresponding
// body, and both bodies rejoin at the implicit continuation point.
//
// The condition is a pipeline expression (PipelineStatement with no assignment) or a
// single identifier — kept as raw text pending AST refinement.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// An if/else statement: <c>if &lt;condition&gt; &lt;then-body&gt; else &lt;else-body&gt;</c>.
/// Either body may be empty. There is no elseif keyword: <c>else if</c> nests an
/// IfStatement inside the Else body.
/// </summary>
public sealed record IfStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <summary>The condition expression (raw BS text form, refined during implementation).</summary>
    public required string Condition { get; init; }

    /// <summary>Body executed when <see cref="Condition"/> is true.</summary>
    public required ImmutableArray<Statement> ThenBody { get; init; } = [];

    /// <summary>
    /// Body executed when <see cref="Condition"/> is false. Empty when the source had
    /// no <c>else</c> clause.
    /// </summary>
    public ImmutableArray<Statement> ElseBody { get; init; } = [];

    public bool Equals(IfStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (Comment != other.Comment || SourceLine != other.SourceLine) return false;
        if (Condition != other.Condition) return false;
        if (!ThenBody.SequenceEqual(other.ThenBody)) return false;
        if (!ElseBody.SequenceEqual(other.ElseBody)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(Comment);
        hash.Add(SourceLine);
        hash.Add(Condition);
        foreach (var s in ThenBody) hash.Add(s);
        foreach (var s in ElseBody) hash.Add(s);
        return hash.ToHashCode();
    }
}
