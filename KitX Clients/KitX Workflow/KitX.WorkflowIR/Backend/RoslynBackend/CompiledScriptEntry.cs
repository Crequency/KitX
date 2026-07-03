namespace KitX.WorkflowIR.Backend.RoslynBackend;

using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// CompiledScriptEntry — direct port of the legacy
// KitX.Workflow.Compilation.CompiledScriptEntry. Wraps a compiled script instance
// with its collectible AssemblyLoadContext so the assembly can be unloaded when
// the entry is evicted.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Wraps a compiled <see cref="ICompiledBlockScript"/> instance with its
/// <see cref="CollectibleAssemblyLoadContext"/> so the assembly can be unloaded
/// after use.
/// </summary>
internal sealed class CompiledScriptEntry
{
    /// <summary>The compiled script instance.</summary>
    public ICompiledBlockScript Instance { get; }

    private readonly CollectibleAssemblyLoadContext _alc;

    /// <summary>Creates an entry binding the instance to its load context.</summary>
    public CompiledScriptEntry(ICompiledBlockScript instance, CollectibleAssemblyLoadContext alc)
    {
        Instance = instance;
        _alc = alc;
    }

    /// <summary>Always true while not explicitly unloaded.</summary>
    public bool IsAlive => true;

    /// <summary>Unloads the assembly context, releasing the compiled assembly's memory.</summary>
    public void Unload()
    {
        try { _alc.Unload(); }
        catch (Exception ex) { Log.Debug(ex, "[CompiledScriptEntry] Error unloading assembly context"); }
    }
}
