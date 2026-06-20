using System.Runtime.Loader;
using Serilog;

namespace KitX.Workflow.Compilation;

/// <summary>
/// Wraps a compiled script instance with its <see cref="CollectibleAssemblyLoadContext"/>
/// to enable unloading after the script execution context is no longer needed.
/// </summary>
internal class CompiledScriptEntry
{
    /// <summary>
    /// The compiled script instance.
    /// </summary>
    public ICompiledBlockScript Instance { get; }

    private readonly CollectibleAssemblyLoadContext _alc;

    /// <summary>
    /// Initializes a new compiled script entry.
    /// </summary>
    /// <param name="instance">The compiled script instance.</param>
    /// <param name="alc">The collectible assembly load context.</param>
    public CompiledScriptEntry(ICompiledBlockScript instance, CollectibleAssemblyLoadContext alc)
    {
        Instance = instance;
        _alc = alc;
    }

    /// <summary>
    /// Gets whether the entry is alive (always true while not explicitly unloaded).
    /// </summary>
    public bool IsAlive => true;

    /// <summary>
    /// Unloads the assembly context, releasing all memory associated with the compiled script.
    /// </summary>
    public void Unload()
    {
        try
        {
            _alc.Unload();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[CompiledScriptEntry] Error unloading assembly context");
        }
    }
}
