namespace KitX.Workflow.CFG;

/// <summary>
/// A constant declaration from #ConstBlock. Preserves both the raw source
/// representation (<see cref="InitialValueExpression"/>) and the evaluated
/// value (<see cref="DefaultValue"/>) to ensure correct round-tripping
/// of string/char literals.
/// </summary>
public class ConstDeclaration
{
    /// <summary>Variable name (e.g. "bfCode", "memorySize").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type name (e.g. "string", "int", "char").</summary>
    public string Type { get; set; } = "object";

    /// <summary>
    /// The raw C# source representation of the initial value expression.
    /// Preserves quoting and escaping (e.g. <c>"\"hello\""</c>, <c>"'\0'"</c>).
    /// Preferred over <see cref="DefaultValue"/> for serialization.
    /// </summary>
    public string? InitialValueExpression { get; set; }

    /// <summary>
    /// The evaluated .NET value of the constant. For strings, this is
    /// the string content WITHOUT quotes. For chars, the char value.
    /// Used during execution but NOT for serialization (use
    /// <see cref="InitialValueExpression"/> instead to preserve quotes).
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>Whether this constant has an initial value assignment.</summary>
    public bool HasInitialValue => DefaultValue != null || !string.IsNullOrEmpty(InitialValueExpression);
}