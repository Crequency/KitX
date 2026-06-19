using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace KitX.Workflow.Contract.Models;

/// <summary>
/// Expression statement
/// </summary>
public class ExpressionStatement : BlockStatement
{
    /// <summary>
    /// The expression to execute
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// The pre-parsed invocation when this statement was extracted from source by
    /// <c>BlockStatementExtractor</c>. Lets <c>BS2CFGConverter</c> skip re-parsing
    /// <see cref="Expression"/> (the double-parse smell). Null when the statement was built
    /// programmatically (e.g. by <c>CFG2BSConverter</c> from a CFG); <c>BS2CFGConverter</c>
    /// then falls back to parsing <see cref="Expression"/>. Transient — not preserved across
    /// BlockScript text serialization.
    /// </summary>
    public InvocationExpressionSyntax? ParsedInvocation { get; set; }

    /// <summary>
    /// The assigned variable when <see cref="ParsedInvocation"/> came from an assignment
    /// statement (e.g. <c>x = Func(...)</c>); null for bare expression statements. Mirrors the
    /// <c>assignedVar</c> that <c>ExprUtils.ParseStatement</c> would derive, avoiding re-derivation.
    /// </summary>
    public string? AssignedVariable { get; set; }
}