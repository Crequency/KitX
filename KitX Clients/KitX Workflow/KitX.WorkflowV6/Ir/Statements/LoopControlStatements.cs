namespace KitX.WorkflowV6.Ir.Statements;

// ─────────────────────────────────────────────────────────────────────────────
// Loop-control escapes — break / continue / exit (discussion notes §3.3 #6–#8).
//
// These are the *structured* replacements for Goto. break and continue escape the
// enclosing loop (forEach / while) lexically; exit terminates the workflow (the v5
// "Break" builtin, renamed because "Break" was already overloaded by loop-break).
//
// Whether break/continue take a label (for breaking out of nested loops, discussion
// notes §10.2) is open. The Label field is reserved here so the implementation phase
// has a concrete place to add it.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Breaks out of the enclosing loop (forEach / while). Targets the nearest enclosing
/// loop unless <see cref="Label"/> names an outer loop (open design point, §10.2).
/// </summary>
public sealed record BreakStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <summary>Optional label of the loop to break out of (nested-loop break, §10.2).</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Skips to the next iteration of the enclosing loop (forEach / while). Targets the
/// nearest enclosing loop unless <see cref="Label"/> names an outer loop.
/// </summary>
public sealed record ContinueStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <summary>Optional label of the loop to continue (nested-loop continue, §10.2).</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Terminates the workflow. Maps to <c>return</c> in the generated structured C#.
/// This is the v5 "Break" builtin renamed to avoid the loop-break name clash
/// (discussion notes §3.3 #8).
/// </summary>
public sealed record ExitStatement : KitX.WorkflowV6.Ir.Statement
{
    /// <summary>Optional exit code / status payload (refined during implementation).</summary>
    public string? Reason { get; init; }
}
