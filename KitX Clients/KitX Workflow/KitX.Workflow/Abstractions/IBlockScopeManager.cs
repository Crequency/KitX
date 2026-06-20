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