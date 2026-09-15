namespace KitX.WorkflowV6.Hosting;

/// <summary>
/// Configuration options for the KitX.WorkflowV6 service graph. Injected as a
/// singleton so the default execution backend (<see cref="Backend.RoslynBackend.StructuredRoslynBackend"/>)
/// reads the ScriptCompiler cache capacity from configuration at startup.
/// </summary>
public class WorkflowV6Options
{
    /// <summary>
    /// Maximum number of compiled workflow assemblies kept in the ScriptCompiler
    /// in-memory LRU cache. Larger values trade memory for fewer recompilations.
    /// Defaults to 256.
    /// </summary>
    public int ScriptCompilerCacheCapacity { get; set; } = 256;
}
