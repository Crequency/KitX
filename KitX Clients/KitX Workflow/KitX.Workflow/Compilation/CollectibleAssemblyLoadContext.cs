using System.Runtime.Loader;

namespace KitX.Workflow.Compilation;

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