namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Variable scope implementation
/// </summary>
public class BlockScope : IBlockScope
{
    private readonly Dictionary<string, object?> _variables = new();
    private readonly bool _isGlobal;

    /// <summary>
    /// Creates a new block scope
    /// </summary>
    public BlockScope(string blockName, bool isGlobal)
    {
        BlockName = blockName;
        _isGlobal = isGlobal;
    }

    /// <summary>
    /// Name of the block this scope belongs to
    /// </summary>
    public string BlockName { get; }

    /// <summary>
    /// Whether this is the global scope
    /// </summary>
    public bool IsGlobal => _isGlobal;

    /// <summary>
    /// Gets a variable value
    /// </summary>
    public object? GetVariable(string name)
    {
        return _variables.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>
    /// Sets a variable value
    /// </summary>
    public void SetVariable(string name, object? value)
    {
        _variables[name] = value;
    }

    /// <summary>
    /// Checks if a variable exists in this scope
    /// </summary>
    public bool HasVariable(string name)
    {
        return _variables.ContainsKey(name);
    }

    /// <summary>
    /// Gets all variables in this scope
    /// </summary>
    public Dictionary<string, object?> GetAllVariables()
    {
        return new Dictionary<string, object?>(_variables);
    }
}