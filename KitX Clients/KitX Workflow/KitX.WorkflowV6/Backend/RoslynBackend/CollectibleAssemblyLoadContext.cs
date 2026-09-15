namespace KitX.WorkflowV6.Backend.RoslynBackend;

using System.Runtime.Loader;
using KitX.WorkflowV6.Backend.Runtime;

// ─────────────────────────────────────────────────────────────────────────────
// CollectibleAssemblyLoadContext — direct port of v5.1's
// KitX.WorkflowIR.Backend.RoslynBackend.CollectibleAssemblyLoadContext. Collectible
// so compiled workflow assemblies unload after use (preventing leaks in long-running
// sessions). Each compiled script gets its own context.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A collectible <see cref="AssemblyLoadContext"/> that allows compiled workflow
/// assemblies to be unloaded after use.
/// </summary>
internal sealed class CollectibleAssemblyLoadContext : AssemblyLoadContext
{
    public CollectibleAssemblyLoadContext(string name) : base(name, isCollectible: true) { }
}