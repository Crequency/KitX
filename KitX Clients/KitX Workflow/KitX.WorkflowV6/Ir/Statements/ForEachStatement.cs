namespace KitX.WorkflowV6.Ir.Statements;

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Ir.Ast;

// ─────────────────────────────────────────────────────────────────────────────
// ForEachStatement — structured collection iteration (discussion notes §3.3 #4,
// §十二-G for BP pin layout).
//
// This is the answer to the ForLoop retirement RFC (see ForLoop-Retirement-And-Each-Design.md):
// the v5 ForLoop(counter, "LoopBody", "LoopEnd") control-flow terminator with its
// implicit counter var and manual Goto("LoopBody") re-entry is replaced by a
// structured <c>forEach list as item { body }</c>. The iteration variable is a
/// *real* input of the body (not a string-var-name injection); the back edge is
// implicit (no Goto); and the "statements that should run once" no longer live
// inside the loop body, so the v5 footgun ("one-shot statement re-executed by
// the Goto loop-back") cannot occur.
//
// BP pin layout (§十二-G): 1 data input (list) + 1 data output (Current element) +
// 2 Exec outputs (Body / End). The Current-element data pin is the fix for the
// v5.1 itemVar string-injection anti-pattern: the body subgraph reads the current
// element from a real data pin, not from a runtime-injected string-named variable.
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
    /// <inheritdoc/>
    public override KitX.WorkflowV6.Ir.StatementKind Kind =>
        KitX.WorkflowV6.Ir.StatementKind.ForEach;

    /// <summary>
    /// The collection-producing expression. A <see cref="KsNode"/> — typically a
    /// <see cref="KsCall"/> to <c>Range(...)</c> or a <see cref="KsIdentifier"/> referencing
    /// a Json array PubVar. Lowered to a typed <c>IEnumerable&lt;T&gt;</c> / array when
    /// the backend's type-inference pass decides an element type (currently the codegen
    /// emits <c>foreach (var item in ...)</c>; a future strong-typing pass would surface
    /// the element type via <see cref="KitX.WorkflowV6.Ir.Lowering.LoweringResult"/>).
    /// </summary>
    public required KsNode Source { get; init; }

    /// <summary>The name of the element binding inside the body.</summary>
    public required string ItemName { get; init; }

    /// <summary>The body executed per element.</summary>
    public required ImmutableArray<Statement> Body { get; init; } = [];

    public bool Equals(ForEachStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (LeadingComment != other.LeadingComment) return false;
        if (TrailingComment != other.TrailingComment) return false;
        if (!Source.Equals(other.Source)) return false;
        if (ItemName != other.ItemName) return false;
        if (!Body.SequenceEqual(other.Body)) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(LeadingComment);
        hash.Add(TrailingComment);
        hash.Add(Source);
        hash.Add(ItemName);
        foreach (var s in Body) hash.Add(s);
        return hash.ToHashCode();
    }
}