using System.Collections.Generic;

namespace KitX.Workflow.Abstractions;

/// <summary>
/// Block scope manager interface — manages variable scoping.
/// Internal to the workflow pipeline.
/// </summary>
public interface IBlockScopeManager
{
    /// <summary>
    /// Gets the global (ConstBlock) scope
    /// </summary>
    IBlockScope GlobalScope { get; }

    /// <summary>
    /// Resolves a variable name to its value (searches local then global)
    /// </summary>
    object? ResolveVariable(string name);

    /// <summary>
    /// Sets a variable value in the appropriate scope
    /// </summary>
    void SetVariable(string name, object? value, bool global = false);

    /// <summary>
    /// Checks if a variable exists in any scope
    /// </summary>
    bool HasVariable(string name);

    /// <summary>
    /// Clears all local scopes (called between executions)
    /// </summary>
    void ClearLocalScopes();
}

/// <summary>
/// Variable scope interface.
/// Internal to the workflow pipeline.
/// </summary>
public interface IBlockScope
{
    /// <summary>
    /// Name of the block this scope belongs to
    /// </summary>
    string BlockName { get; }

    /// <summary>
    /// Whether this is the global scope
    /// </summary>
    bool IsGlobal { get; }

    /// <summary>
    /// Gets a variable value
    /// </summary>
    object? GetVariable(string name);

    /// <summary>
    /// Sets a variable value
    /// </summary>
    void SetVariable(string name, object? value);

    /// <summary>
    /// Checks if a variable exists in this scope
    /// </summary>
    bool HasVariable(string name);

    /// <summary>
    /// Gets all variables in this scope
    /// </summary>
    Dictionary<string, object?> GetAllVariables();
}