namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Reflection;
using System.Runtime.Loader;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// CompiledScriptEntry — wraps a compiled assembly with its collectible
// AssemblyLoadContext so the assembly can be unloaded when the cache entry
// is evicted. Adapted from v5.1 WorkflowIR's CompiledScriptEntry.
//
// v6 difference: caches the Assembly (not an ICompiledBlockScript instance)
// because v6 instantiates the G class per-execution to wire different debugger
// configurations. The Assembly is reusable across executions with different
// debuggers — only the instance differs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Wraps a compiled assembly with its collectible AssemblyLoadContext for
/// cache management and unloading.
/// </summary>
internal sealed class CompiledScriptEntry
{
    public Assembly Assembly { get; }

    private readonly CollectibleAssemblyLoadContext _alc;

    public CompiledScriptEntry(Assembly assembly, CollectibleAssemblyLoadContext alc)
    {
        Assembly = assembly;
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