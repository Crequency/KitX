using KitX.Workflow.Models;

namespace KitX.Workflow.Models.Statements;

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
    /// The pre-parsed BS expression when this statement was extracted from source by
    /// <c>BlockStatementExtractor</c> (via <c>BSExpressionAdapter.FromRoslyn</c>), or built
    /// programmatically by <c>BP2CFGConverter.GenerateBlockStatement</c>. Lets
    /// <c>BS2CFGConverter</c> walk the structured AST directly instead of re-parsing
    /// <see cref="Expression"/> text. Carries whatever shape the RHS has — a <see cref="BSCall"/>,
    /// <see cref="BSBinary"/> (+ chain), or <see cref="BSAssignment"/>. Null only when the
    /// statement has no analyzable expression body (pure data nodes). Transient — not preserved
    /// across BlockScript text serialization.
    /// </summary>
    public BSExpression? ParsedExpression { get; set; }

    /// <summary>
    /// The assigned variable when <see cref="ParsedExpression"/> came from an assignment
    /// statement (e.g. <c>x = Func(...)</c>); null for bare expression statements.
    /// </summary>
    public string? AssignedVariable { get; set; }
}
