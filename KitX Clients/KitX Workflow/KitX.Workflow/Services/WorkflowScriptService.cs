using KitX.Core.Contract.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Hosting;
using KitX.Shared.CSharp.Plugin;
using Serilog;

namespace KitX.Workflow.Services;

/// <summary>
/// Static factory container for the workflow service graph.
///
/// Formerly an aggregate facade that implemented IWorkflowManagementService /
/// IWorkflowPluginService / IBlockScriptService / IBlockScriptPipelineService by forwarding
/// to the real implementations. Those interface implementations were dead code — DI never
/// resolved WorkflowScriptService itself as any of those interfaces (it registered
/// BlockScriptService for IBlockScriptService, and the management/plugin services via the
/// static factory properties below). The facade interface implementations have been removed;
/// what remains is the static service-graph factory that the DI lambdas in
/// ServiceCollectionExtensions still reference (RuntimeState / PluginServiceInstance /
/// ManagementServiceInstance).
/// </summary>
public class WorkflowScriptService
{
    /// <summary>
    /// Shared runtime state across all workflow services. Registered in DI as a singleton
    /// and also exposed here for the static factory properties below.
    /// </summary>
    private static readonly WorkflowRuntimeState SharedState = new();

    /// <summary>
    /// The plugin service — built once (depends only on SharedState).
    /// </summary>
    private static readonly IWorkflowPluginService PluginService = new WorkflowPluginService(SharedState);

    /// <summary>
    /// The management service — lazily built once BlockScriptService is available.
    /// </summary>
    private static IWorkflowManagementService? _managementService;

    static WorkflowScriptService()
    {
        // PluginService initialized inline above. ManagementService is lazy (see property).
    }

    /// <summary>Exposes the shared runtime state for DI registration.</summary>
    internal static WorkflowRuntimeState RuntimeState => SharedState;

    /// <summary>Exposes IWorkflowPluginService for DI registration.</summary>
    internal static IWorkflowPluginService PluginServiceInstance => PluginService;

    /// <summary>
    /// Gets the ManagementService, creating it lazily once BlockScriptService is available.
    /// BlockScriptService is resolved from DI by the caller (ServiceCollectionExtensions),
    /// but the management service here is built against the static SharedState + the
    /// BlockScriptService that DI constructs — wired via ManagementServiceInstance.
    /// </summary>
    private static IWorkflowManagementService ManagementService =>
        _managementService ??= new WorkflowManagementService(SharedState, ResolveBlockScriptService());

    /// <summary>Exposes IWorkflowManagementService for DI registration.</summary>
    internal static IWorkflowManagementService ManagementServiceInstance => ManagementService;

    /// <summary>
    /// Resolves the DI-registered BlockScriptService. The DI factory in
    /// ServiceCollectionExtensions constructs it with RealPluginManager; here we fetch the
    /// IBlockScriptService that DI registered (which is that same BlockScriptService).
    /// </summary>
    private static BlockScriptService ResolveBlockScriptService()
    {
        // The DI-registered IBlockScriptService is a BlockScriptService instance; cast to
        // reach the parsed-model methods that WorkflowManagementService needs.
        var bss = (BlockScriptService)ServiceLocator.GetRequiredService<IBlockScriptService>();
        return bss;
    }

    private WorkflowScriptService() { }
}