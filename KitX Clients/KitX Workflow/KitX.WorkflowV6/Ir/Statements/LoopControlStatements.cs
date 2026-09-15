namespace KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// Loop-control escapes — break / continue (discussion notes §3.3 #6–#7).
//
// These are the *structured* replacements for Goto. break and continue escape the
// enclosing loop (forEach / while) lexically. A top-level workflow ends when its
// statement sequence runs out (implicit return) — there is no explicit exit/return
// keyword: an exit would be a non-local "program-level Goto" that conflicts with the
// structured principle, and every early-exit scenario is expressible via if-branches.
//
// Per discussion notes §十二-D: break/continue do NOT take a label (no labeled-break
// / labeled-continue). Escaping an outer loop requires refactoring (extract to a
// helper, or use a flag). This keeps the language firmly structured — no goto-in-
// disguise. The <see cref="BreakStatement.Label"/> / <see cref="ContinueStatement.Label"/>
// fields are reserved here only so the IR shape is forward-compatible if a future
// revision reverses §十二-D; they default to null and the v6 parser will reject any
// non-null value until such a revision.
//
// These two are first-class IR statement kinds per §十二-K (NOT IBuiltinFunction):
// the indented parser builds them directly, and Phase 4 codegen lowers them to the
// C# <c>break;</c> / <c>continue;</c> keywords.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Breaks out of the enclosing loop (forEach / while). Targets the nearest enclosing
/// loop (§十二-D: no labeled break — <see cref="Label"/> is reserved for a future
/// revision and must be null today).
/// </summary>
public sealed record BreakStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <inheritdoc/>
    public override KitX.WorkflowV6.Ir.StatementKind Kind =>
        KitX.WorkflowV6.Ir.StatementKind.Break;

    /// <summary>
    /// Optional label of the loop to break out of. Reserved for a future labeled-break
    /// feature (§十二-D); must be null today — the v6 parser rejects any non-null value.
    /// </summary>
    public string? Label { get; init; }

    public bool Equals(BreakStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (LeadingComment != other.LeadingComment) return false;
        if (TrailingComment != other.TrailingComment) return false;
        if (Label != other.Label) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(LeadingComment);
        hash.Add(TrailingComment);
        hash.Add(Label);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Skips to the next iteration of the enclosing loop (forEach / while). Targets the
/// nearest enclosing loop (§十二-D: no labeled continue).
/// </summary>
public sealed record ContinueStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <inheritdoc/>
    public override KitX.WorkflowV6.Ir.StatementKind Kind =>
        KitX.WorkflowV6.Ir.StatementKind.Continue;

    /// <summary>Optional label of the loop to continue. Reserved (§十二-D); must be null today.</summary>
    public string? Label { get; init; }

    public bool Equals(ContinueStatement? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Fingerprint.Equals(other.Fingerprint) == false) return false;
        if (LeadingComment != other.LeadingComment) return false;
        if (TrailingComment != other.TrailingComment) return false;
        if (Label != other.Label) return false;
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(LeadingComment);
        hash.Add(TrailingComment);
        hash.Add(Label);
        return hash.ToHashCode();
    }
}