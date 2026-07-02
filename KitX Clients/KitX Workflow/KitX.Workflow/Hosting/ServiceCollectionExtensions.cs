using Microsoft.Extensions.DependencyInjection;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.Event;
using KitX.Workflow;
using KitX.Workflow.BlockScripting;
using KitX.Workflow.Blueprint;
using KitX.Workflow.Conversion;
using KitX.Workflow.Services;
using Serilog;

namespace KitX.Workflow.Hosting;

/// <summary>
/// DI registration extensions for the KitX.Workflow library.
/// Host applications (KitX.Core) call <see cref="AddKitXWorkflow"/> to register
/// the full workflow service graph: BlockScript parsing/execution, blueprint
/// conversion pipeline, builtin-function registry, and the plugin/trigger bridges.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all KitX.Workflow services in the dependency injection container.
    /// The host must have already registered the shared Core services that workflow
    /// depends on (IPluginServer, IDeviceServer, IEventService, IDeviceHttpClient,
    /// IDeviceDiscoveryService) before calling this.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKitXWorkflow(this IServiceCollection services)
    {
        Log.Information("[AddKitXWorkflow] Registering workflow services...");

        // IBlockScriptService is created via factory. It receives the same DI-registered,
        // plugin-manager-wired IBlockScriptExecutor singleton (registered below) so the trigger
        // execution path runs plugins through the wired executor, not a bare new instance.
        // WorkflowScriptService facade is kept as the backward-compat singleton graph.
        services.AddSingleton<IBlockScriptService>(provider =>
        {
            var state = WorkflowScriptService.RuntimeState;
            var rpm = provider.GetRequiredService<RealPluginManager>();
            var executor = provider.GetRequiredService<IBlockScriptExecutor>();
            var service = new BlockScriptService(state, executor);
            Log.Information("[DI] IBlockScriptService created with RealPluginManager + wired executor. RPM HashCode: {HashCode}", rpm.GetHashCode());
            return service;
        });
        // IBlockScriptPipelineService was removed (zero interface-type consumers;
        // WorkflowManagementService uses the concrete BlockScriptService directly).
        services.AddSingleton<IWorkflowPluginService>(sp => WorkflowScriptService.PluginServiceInstance);
        services.AddSingleton<IWorkflowManagementService>(sp => WorkflowScriptService.ManagementServiceInstance);

        // RealPluginManager — singleton so PluginsServer and WorkflowScriptService share
        // the same instance, ensuring plugin connection events are properly received.
        services.AddSingleton<RealPluginManager>(provider =>
        {
            var pluginServer = provider.GetRequiredService<IPluginServer>();
            var eventService = provider.GetRequiredService<IEventService>();
            var deviceDiscoveryService = provider.GetRequiredService<IDeviceDiscoveryService>();
            var deviceServer = provider.GetRequiredService<IDeviceServer>();
            return new RealPluginManager(pluginServer, eventService, deviceDiscoveryService, deviceServer, provider.GetRequiredService<IDeviceHttpClient>());
        });
        // Expose the same RealPluginManager instance under the bridge contract so the
        // host (Dashboard) can pre-resolve it to force eager singleton construction.
        services.AddSingleton<IRealPluginManagerBridge>(sp => sp.GetRequiredService<RealPluginManager>());

        // Block Script Services
        services.AddSingleton<IBlockScriptParser>(provider =>
        {
            var funcRegistry = provider.GetService<BuiltinFunctionRegistry>();
            return new BlockScriptParser(funcRegistry);
        });

        services.AddSingleton<IBlockScriptExecutor>(provider =>
        {
            var service = new BlockScriptExecutor();
            try
            {
                var pluginManager = provider.GetRequiredService<RealPluginManager>();
                service.SetPluginManager(pluginManager);
                Log.Information("[DI] IBlockScriptExecutor: RealPluginManager HashCode = {HashCode}", pluginManager.GetHashCode());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DI] Could not initialize BlockScriptExecutor with RealPluginManager from DI container");
            }
            return service;
        });

        services.AddSingleton<IBlockScopeManager, BlockScopeManager>();

        // Blueprint sub-services (must be registered before IBlueprintService)
        // Discover and register all IBuiltinFunctionDefinition implementations
        var functionRegistry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        services.AddSingleton(functionRegistry);

        // NodeRegistry with function registry for dynamic node creation
        services.AddSingleton<INodeRegistry>(provider =>
        {
            var reg = provider.GetRequiredService<BuiltinFunctionRegistry>();
            return new NodeRegistry(reg);
        });

        services.AddSingleton<ILayoutService, LayoutService>();
        services.AddSingleton<IBlueprintRenderDataService, BlueprintRenderDataService>();

        // v5.1: BP converters removed. BS → CFG → C# is the canonical path.
        // BP graph rendering is handled by CFGGraphRenderer (G-3); CFG → BP minimal-change
        // sync is driven by CfgDiffer. Both are implemented and registered below.

        // v5.2: IBlueprintService removed — replaced by IWorkflowSession / IBsSyncService / IBpEditApplier / ICfgBpRenderer / ICfgBsRenderer / ICfgExecutor.

        // v5.1 G-3 + minimal-change sync: render CFG → Blueprint graph and diff two CFGs.
        services.AddSingleton<ICFGGraphRenderer, CFGGraphRenderer>();
        services.AddSingleton<ICFGDiffer, CfgDiffer>();

        // v5.2 sync engine (G1-G8): new session-based interfaces — registered as stubs
        // until their implementations land in subsequent batches.
        services.AddSingleton<ICfgDiffApplier, CfgDiffApplier>();
        services.AddSingleton<IBsSyncService, BsSyncService>();
        services.AddSingleton<IBpEditApplier, BpEditApplier>();
        services.AddSingleton<ICfgBsRenderer, CfgBsRenderer>();
        services.AddSingleton<ICfgBpRenderer, CfgBpRenderer>();
        services.AddSingleton<ICfgExecutor>(_ =>
            throw new NotImplementedException("ICfgExecutor not yet implemented"));

        // Workflow Storage Service
        services.AddSingleton<IWorkflowStorageService, WorkflowStorageService>();

        // Trigger Manager (constructor takes IPluginServer)
        services.AddSingleton<TriggerManager>();
        // Expose TriggerManager under the ITriggerManager contract too
        services.AddSingleton<ITriggerManager>(sp => sp.GetRequiredService<TriggerManager>());

        Log.Information("[AddKitXWorkflow] Workflow services registered.");
        return services;
    }
}
