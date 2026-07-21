namespace KitX.WorkflowV6.Ir.Statements;

using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// WhileStatement — structured conditional loop (discussion notes §3.3 #5, §十二-E).
//
// Carried as a first-class IR statement kind per §十二-K (NOT an IBuiltinFunction).
// Whether MVP includes it is open (discussion notes §10.1: "while 是否需要"); the
// shape is reserved here so Phase 4 codegen can emit `while` directly.
//
// Unlike forEach, while carries a dynamic condition and no element binding. Break
// and Continue escape the body; see <see cref="BreakStatement"/> and
// <see cref="ContinueStatement"/>. Per §十二-B the condition is always a function call
// or identifier (no comparison operators), so <see cref="Condition"/> is a BsNode.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A while statement: <c>while &lt;condition&gt; { body }</c>. The body repeats while
/// <see cref="Condition"/> evaluates true.
/// </summary>
public sealed record WhileStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <inheritdoc/>
    public override KitX.WorkflowV6.Ir.StatementKind Kind =>
        KitX.WorkflowV6.Ir.StatementKind.While;

    /// <summary>
    /// The loop-continuation condition. A <see cref="BsNode"/> — typically a
    /// <see cref="BsCall"/> to <c>HelperFuncCompare</c> or a <see cref="BsIdentifier"/>
    /// referencing a bool PubVar (comparisons are function-call-only per §十二-B).
    /// </summary>
    public required BsNode Condition { get; init; }

    /// <summary>The body executed while <see cref="Condition"/> holds.</summary>
    public required ImmutableArray<Statement> Body { get; init; } = [];

    public bool Equals(WhileStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (Comment != other.Comment) return false;
        if (!Condition.Equals(other.Condition)) return false;
        if (!Body.SequenceEqual(other.Body)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(Comment);
                hash.Add(Condition);
        foreach (var s in Body) hash.Add(s);
        return hash.ToHashCode();
    }
}