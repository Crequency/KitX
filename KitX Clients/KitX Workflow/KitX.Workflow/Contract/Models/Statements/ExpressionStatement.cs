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
}