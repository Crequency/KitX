namespace KitX.Workflow.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// BlockScope — a single variable scope (one block activation, or the global scope).
//
// Migrated verbatim from the legacy KitX.Workflow.BlockScripting.BlockScope, with
// only the namespace changed. The legacy BlockScopeManager searched local scopes in
// reverse-insertion order then fell back to global; that semantics is preserved
// here so existing scripts resolve variables identically.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single variable scope: a name→value dictionary with a block name and a
/// global/local flag. The runtime stacks one per block activation; the global
/// scope holds ConstBlock + PubVarBlock values.
/// </summary>
public sealed class BlockScope
{
    private readonly Dictionary<string, object?> _variables = new();
    private readonly bool _isGlobal;

    /// <summary>Creates a new scope for the named block.</summary>
    public BlockScope(string blockName, bool isGlobal)
    {
        BlockName = blockName;
        _isGlobal = isGlobal;
    }

    /// <summary>Name of the block this scope belongs to.</summary>
    public string BlockName { get; }

    /// <summary>Whether this is the global scope.</summary>
    public bool IsGlobal => _isGlobal;

    /// <summary>Gets a variable value (null when absent).</summary>
    public object? GetVariable(string name)
        => _variables.TryGetValue(name, out var value) ? value : null;

    /// <summary>Sets a variable value.</summary>
    public void SetVariable(string name, object? value) => _variables[name] = value;

    /// <summary>True when the variable exists in this scope.</summary>
    public bool HasVariable(string name) => _variables.ContainsKey(name);

    /// <summary>Returns a snapshot of all variables in this scope.</summary>
    public Dictionary<string, object?> GetAllVariables() => new(_variables);
}
