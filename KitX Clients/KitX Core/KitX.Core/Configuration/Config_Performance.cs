using KitX.Core.Contract.Configuration;

namespace KitX.Core.Configuration;

/// <summary>
/// Performance configuration section
/// </summary>
public class Config_Performance : IPerformanceConf
{
    /// <summary>
    /// Maximum number of compiled workflow assemblies kept in the WorkflowV6
    /// ScriptCompiler in-memory LRU cache. Larger values trade memory for fewer
    /// recompilations. Applies at startup.
    /// </summary>
    public int ScriptCompilerCacheCapacity { get; set; } = 256;

    /// <summary>
    /// Maximum number of Completed instances retained by the ToolKit instance manager.
    /// Applies at startup.
    /// </summary>
    public int CompletedInstanceCap { get; set; } = 200;
}
