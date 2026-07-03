namespace KitX.Workflow.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// BlockScopeManager — manages variable scoping for a script run.
//
// Migrated verbatim (semantics preserved) from the legacy
// KitX.Workflow.BlockScripting.BlockScopeManager. The legacy type implemented an
// IBlockScopeManager interface that lived in the workflow library's Abstractions
// namespace; the new runtime owns its own scope types (no shared abstraction
// needed — the scope manager is an internal runtime collaborator of
// ExecutionGlobals, not a contract).
//
// Resolution order: local scopes searched most-recent-first, then the global
// scope. This matches the legacy behaviour so existing scripts resolve
// shadowing identically.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages the variable scope stack for a script run: one global scope plus N
/// local (block-activation) scopes. Resolution searches locals most-recent-first,
/// then the global scope.
/// </summary>
public sealed class BlockScopeManager
{
    private readonly BlockScope _globalScope;
    private readonly Dictionary<string, BlockScope> _localScopes = new();

    /// <summary>Creates a new scope manager with an empty global scope.</summary>
    public BlockScopeManager()
    {
        _globalScope = new BlockScope("Global", isGlobal: true);
    }

    /// <summary>The global (ConstBlock + PubVarBlock) scope.</summary>
    public BlockScope GlobalScope => _globalScope;

    /// <summary>Resolves a variable: locals most-recent-first, then global. Null if absent.</summary>
    public object? ResolveVariable(string name)
    {
        foreach (var scope in _localScopes.Values.Reverse())
            if (scope.HasVariable(name))
                return scope.GetVariable(name);

        return _globalScope.HasVariable(name) ? _globalScope.GetVariable(name) : null;
    }

    /// <summary>Sets a variable, into the most-recent local scope (or global when none).</summary>
    public void SetVariable(string name, object? value, bool global = false)
    {
        if (global)
        {
            _globalScope.SetVariable(name, value);
            return;
        }
        var lastScope = _localScopes.Values.LastOrDefault();
        if (lastScope != null)
            lastScope.SetVariable(name, value);
        else
            _globalScope.SetVariable(name, value);
    }

    /// <summary>True when the variable exists in any scope.</summary>
    public bool HasVariable(string name)
    {
        foreach (var scope in _localScopes.Values.Reverse())
            if (scope.HasVariable(name)) return true;
        return _globalScope.HasVariable(name);
    }

    /// <summary>Clears all local scopes (between executions).</summary>
    public void ClearLocalScopes() => _localScopes.Clear();
}
