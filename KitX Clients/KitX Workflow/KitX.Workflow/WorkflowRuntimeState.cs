using Csharpell.Core;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Shared.CSharp.Plugin;

namespace KitX.Workflow;

/// <summary>
/// Shared state container for workflow runtime services.
/// Holds all mutable state used across IWorkflowManagementService,
/// IScriptExecutionService, IWorkflowPluginService, and IBlockScriptService.
/// </summary>
internal class WorkflowRuntimeState
{
    /// <summary>
    /// In-memory workflow registry.
    /// </summary>
    internal readonly List<IWorkflowCase> Workflows = new();

    /// <summary>
    /// CSharpScript engine instance for script execution.
    /// </summary>
    internal CSharpScriptEngine? Engine;

    /// <summary>
    /// Whether the plugin manager has been initialized.
    /// </summary>
    internal bool IsParserInitialized;

    /// <summary>
    /// Available plugins for workflow execution.
    /// </summary>
    internal List<PluginInfo> AvailablePlugins { get; set; } = new();

    /// <summary>
    /// Block script parser instance.
    /// </summary>
    internal BlockScriptParser? BlockScriptParser;

    /// <summary>
    /// Block script executor instance.
    /// </summary>
    internal BlockScriptExecutor? BlockScriptExecutor;
}
