using KitX.Core.Contract.Workflow;

namespace KitX.Workflow.BlockScripting;

/// <summary>
/// Block scope manager implementation - manages variable scoping for block scripts
/// </summary>
public class BlockScopeManager : IBlockScopeManager
{
    private readonly BlockScope _globalScope;
    private readonly Dictionary<string, BlockScope> _localScopes = new();

    /// <summary>
    /// Creates a new block scope manager
    /// </summary>
    public BlockScopeManager()
    {
        _globalScope = new BlockScope("Global", isGlobal: true);
    }

    /// <summary>
    /// Gets the global (ConstBlock) scope
    /// </summary>
    public IBlockScope GlobalScope => _globalScope;

    /// <summary>
    /// Resolves a variable name to its value (searches local then global)
    /// </summary>
    public object? ResolveVariable(string name)
    {
        // Search local scopes in reverse order (most recent first)
        foreach (var scope in _localScopes.Values.Reverse())
        {
            if (scope.HasVariable(name))
                return scope.GetVariable(name);
        }

        // Fall back to global scope
        return _globalScope.HasVariable(name) ? _globalScope.GetVariable(name) : null;
    }

    /// <summary>
    /// Sets a variable value in the appropriate scope
    /// </summary>
    public void SetVariable(string name, object? value, bool global = false)
    {
        if (global)
        {
            _globalScope.SetVariable(name, value);
        }
        else
        {
            // Find the most recent local scope and set there
            var lastScope = _localScopes.Values.LastOrDefault();
            if (lastScope != null)
            {
                lastScope.SetVariable(name, value);
            }
            else
            {
                // No local scope exists, set in global
                _globalScope.SetVariable(name, value);
            }
        }
    }

    /// <summary>
    /// Checks if a variable exists in any scope
    /// </summary>
    public bool HasVariable(string name)
    {
        foreach (var scope in _localScopes.Values.Reverse())
        {
            if (scope.HasVariable(name))
                return true;
        }
        return _globalScope.HasVariable(name);
    }

    /// <summary>
    /// Clears all local scopes (called between executions)
    /// </summary>
    public void ClearLocalScopes()
    {
        _localScopes.Clear();
    }

    /// <summary>
    /// Initializes the global scope with variables from ConstBlock and PubVarBlock
    /// </summary>
    public void InitializeGlobalScope(BlockScript script)
    {
        // Initialize from ConstBlock
        if (script.ConstBlock != null)
        {
            foreach (var varDecl in script.ConstBlock.Variables)
            {
                _globalScope.SetVariable(varDecl.Name, varDecl.DefaultValue);
            }
        }

        // Initialize from PubVarBlock
        if (script.PubVarBlock != null)
        {
            foreach (var varDecl in script.PubVarBlock.Variables)
            {
                _globalScope.SetVariable(varDecl.Name, varDecl.DefaultValue);
            }
        }
    }
}
