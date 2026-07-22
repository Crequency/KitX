namespace KitX.WorkflowV6.Ir.Statements;

using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// SwitchStatement — structured N-way dispatch (KScript discussion notes §3.3 #3,
// §十二-H for BP pin layout).
//
// Replaces the v5 Switch(selector, "0", "1", ..., "Default") control-flow terminator
// + the N named target blocks. In v6 the arms are *children* of the SwitchStatement,
// lexically nested under each case label. There is no block-name addressing, no Goto.
//
// Out-of-range behaviour follows v5: the <c>Default</c> arm handles selector values
// outside <c>[0, Arms.Count)</c>.
//
// BP pin layout (§十二-H): 1 data input (selector) + N Exec outputs (0..N-1) +
// 1 Exec output (Default). Each Exec output connects to the subgraph for that arm's
// body; all arms rejoin at the implicit continuation point.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A switch statement: <c>switch &lt;selector&gt; { 0: A; 1: B; default: C }</c>.
/// The selector is an integer index; <see cref="Arms"/> carries arms 0..N-1 in order;
/// <see cref="Default"/> is the fallback body.
/// </summary>
public sealed record SwitchStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <inheritdoc/>
    public override KitX.WorkflowV6.Ir.StatementKind Kind =>
        KitX.WorkflowV6.Ir.StatementKind.Switch;

    /// <summary>
    /// The integer selector expression. A <see cref="KsNode"/> — typically a
    /// <see cref="KsCall"/> or <see cref="KsIdentifier"/>. The selector is evaluated once
    /// and used to index into <see cref="Arms"/>.
    /// </summary>
    public required KsNode Selector { get; init; }

    /// <summary>Ordered arms. <c>Arms[i]</c> is the body executed when the selector equals <c>i</c>.</summary>
    public required ImmutableArray<ImmutableArray<Statement>> Arms { get; init; } = [];

    /// <summary>Fallback body for out-of-range selector values.</summary>
    public ImmutableArray<Statement> Default { get; init; } = [];

    public bool Equals(SwitchStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (Comment != other.Comment) return false;
        if (!Selector.Equals(other.Selector)) return false;
        if (Arms.Length != other.Arms.Length) return false;
        for (int i = 0; i < Arms.Length; i++)
            if (!Arms[i].SequenceEqual(other.Arms[i])) return false;
        if (!Default.SequenceEqual(other.Default)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(Comment);
                hash.Add(Selector);
        foreach (var arm in Arms)
            foreach (var s in arm) hash.Add(s);
        foreach (var s in Default) hash.Add(s);
        return hash.ToHashCode();
    }
}