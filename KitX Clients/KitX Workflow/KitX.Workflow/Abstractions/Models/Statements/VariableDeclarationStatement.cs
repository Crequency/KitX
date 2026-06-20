namespace KitX.Workflow.Abstractions.Models.Statements;

/// <summary>
/// Variable declaration statement
/// </summary>
public class VariableDeclarationStatement : BlockStatement
{
    /// <summary>
    /// The variable declaration
    /// </summary>
    public VariableDeclaration Declaration { get; set; } = new();
}