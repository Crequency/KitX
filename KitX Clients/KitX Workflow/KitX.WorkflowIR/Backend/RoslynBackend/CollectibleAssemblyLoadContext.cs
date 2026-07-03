namespace KitX.WorkflowIR.Backend.RoslynBackend;

using System.Runtime.Loader;

// ─────────────────────────────────────────────────────────────────────────────
// CollectibleAssemblyLoadContext — direct port of the legacy
// KitX.Workflow.Compilation.CollectibleAssemblyLoadContext. Only the namespace
// changed; the collectible-ALC semantics (load compiled assemblies, unload to
// release memory) are identical.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A collectible <see cref="AssemblyLoadContext"/> that allows compiled workflow
/// assemblies to be unloaded after use, preventing memory leaks during long-running
/// sessions. Each compiled script gets its own context.
/// </summary>
internal sealed class CollectibleAssemblyLoadContext : AssemblyLoadContext
{
    /// <summary>Creates a new collectible context with the given name.</summary>
    public CollectibleAssemblyLoadContext(string name) : base(name, isCollectible: true) { }
}
