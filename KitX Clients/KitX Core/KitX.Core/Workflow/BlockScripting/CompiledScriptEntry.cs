using System;
using System.Reflection;
using System.Runtime.Loader;
using KitX.Core.Contract.Workflow;
using Serilog;

namespace KitX.Core.Workflow.BlockScripting;

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

/// <summary>
/// A collectible <see cref="AssemblyLoadContext"/> that allows compiled script assemblies
/// to be unloaded after use, preventing memory leaks during long-running workflow sessions.
/// </summary>
internal class CollectibleAssemblyLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// Initializes a new collectible assembly load context.
    /// </summary>
    /// <param name="name">The name for this context.</param>
    public CollectibleAssemblyLoadContext(string name) : base(name, isCollectible: true) { }
}
