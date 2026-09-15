namespace KitX.WorkflowV6.Ir.Statements;

using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// WhileStatement — structured conditional loop (discussion notes §3.3 #5, §十二-E).
//
// Carried as a first-class IR statement kind per §十二-K (NOT an IBuiltinFunction);
// the codegen emits `while` directly (see StructuredCodegen / DebugCodegen).
//
// Unlike forEach, while carries a dynamic condition and no element binding. Break
// and Continue escape the body; see <see cref="BreakStatement"/> and
// <see cref="ContinueStatement"/>. Per §十二-B the condition is always a function call
// or identifier (no comparison operators), so <see cref="Condition"/> is a KsNode.
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
    /// The loop-continuation condition. A <see cref="KsNode"/> — typically a
    /// <see cref="KsCall"/> to <c>Compare</c> or a <see cref="KsIdentifier"/>
    /// referencing a bool PubVar (comparisons are function-call-only per §十二-B).
    /// </summary>
    public required KsNode Condition { get; init; }

    /// <summary>The body executed while <see cref="Condition"/> holds.</summary>
    public required ImmutableArray<Statement> Body { get; init; } = [];

    public bool Equals(WhileStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (LeadingComment != other.LeadingComment) return false;
        if (TrailingComment != other.TrailingComment) return false;
        if (!Condition.Equals(other.Condition)) return false;
        if (!Body.SequenceEqual(other.Body)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(LeadingComment);
        hash.Add(TrailingComment);
                hash.Add(Condition);
        foreach (var s in Body) hash.Add(s);
        return hash.ToHashCode();
    }
}