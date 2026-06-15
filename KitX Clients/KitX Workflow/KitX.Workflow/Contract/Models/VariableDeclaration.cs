namespace KitX.Workflow.Contract.Models;

/// <summary>
/// Variable declaration
/// </summary>
public class VariableDeclaration
{
    /// <summary>
    /// Variable name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Variable type as string
    /// </summary>
    public string Type { get; set; } = "object";

    /// <summary>
    /// Initial value expression as string (for evaluation at parse time or execution time)
    /// </summary>
    public string? InitialValueExpression { get; set; }

    /// <summary>
    /// Default value (pre-evaluated for const block)
    /// </summary>
    public object? DefaultValue { get; set; }
}