using Microsoft.Extensions.DependencyInjection;
using KitX.Core.Contract.Activity;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Device;
using KitX.Core.Contract.FileWatcher;
using KitX.Core.Contract.Hotkey;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Security;
using KitX.Core.Contract.Statistics;
using KitX.Core.Contract.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Core.Contract.Event;
using KitX.Core.Activity;
using KitX.Core.Announcement;
using KitX.Core.Configuration;
using KitX.Core.Device;
using KitX.Core.FileWatcher;
using KitX.Core.Hotkey;
using KitX.Core.Plugin;
using KitX.Core.Security;
using KitX.Core.Statistics;
using KitX.Core.Tasks;
using KitX.Core.Workflow;
using KitX.Core.Workflow.BlockScripting;
using KitX.Core.Workflow.Blueprint;
using KitX.Core.Event;
using Serilog;

namespace KitX.Core.DI;

/// <summary>
/// Extension methods for configuring KitX Core services in the dependency injection container
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds all KitX Core services to the dependency injection container
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        Log.Information("AddCoreServices started...");

        // Register all core services as singletons
        // These services maintain state and should have only one instance throughout the application lifetime

        // Configuration Services
        Log.Information("Registering IConfigService...");
        services.AddSingleton<IConfigService>(sp => ConfigManager.Instance);

        // Security Services
        Log.Information("Registering IDeviceKeyService and IEncryptionService...");
        services.AddSingleton<IDeviceKeyService, SecurityManager>();
        services.AddSingleton<IEncryptionService, SecurityManager>();

        // Plugin Services
        Log.Information("Registering IPluginService...");
        services.AddSingleton<IPluginService, PluginsManager>();

        // Workflow Services
        Log.Information("Registering workflow services...");
        // Individual service implementations (WorkflowScriptService facade used for backward compatibility)
        // IBlockScriptService is created via factory to inject RealPluginManager from DI
        services.AddSingleton<IBlockScriptService>(provider =>
        {
            var state = WorkflowScriptService.RuntimeState;
            var rpm = provider.GetRequiredService<RealPluginManager>();
            var service = new BlockScriptServiceImpl(state, rpm);
            Log.Information("[DI] IBlockScriptService created with RealPluginManager. HashCode: {HashCode}", rpm.GetHashCode());
            return service;
        });
        services.AddSingleton<IWorkflowPluginService>(sp => WorkflowScriptService.PluginServiceInstance);
        services.AddSingleton<IWorkflowManagementService>(sp => WorkflowScriptService.ManagementServiceInstance);

        // Activity Services
        Log.Information("Registering IActivityService...");
        services.AddSingleton<IActivityService, ActivityManager>();

        // Statistics Services
        Log.Information("Registering IStatisticsService...");
        services.AddSingleton<IStatisticsService, StatisticsManager>();

        // Task Services
        Log.Information("Registering ITasksService...");
        services.AddSingleton<ITasksService, TasksManager>();

        // File Watcher Services
        Log.Information("Registering IFileWatcherService...");
        services.AddSingleton<IFileWatcherService, FileWatcherManager>();

        // Hotkey Services
        Log.Information("Registering IKeyHookService...");
        services.AddSingleton<IKeyHookService, KeyHookManager>();

        // Event Services
        Log.Information("Registering IEventService...");
        services.AddSingleton<IEventService, EventService>();

        // Phase 5: Device and Network Services
        Log.Information("Registering IDeviceDiscoveryService...");
        services.AddSingleton<IDeviceDiscoveryService, DevicesDiscoveryServer>();

        Log.Information("Registering IDeviceServer...");
        services.AddSingleton<IDeviceServer, DevicesServer>();

        Log.Information("Registering IDevicesOrganizer...");
        services.AddSingleton<DevicesOrganizer>();

        Log.Information("Registering IPluginServer...");
        services.AddSingleton<IPluginServer, PluginsServer>();

        // Phase 5: Device HTTP Client (for cross-device plugin invocation)
        Log.Information("Registering IDeviceHttpClient...");
        services.AddSingleton<IDeviceHttpClient, DeviceHttpClient>();

        // RealPluginManager must be registered as singleton so that PluginsServer and WorkflowScriptService
        // use the same instance. This ensures plugin connection events are properly received.
        Log.Information("Registering RealPluginManager...");
        services.AddSingleton<RealPluginManager>(provider =>
        {
            var pluginServer = provider.GetRequiredService<IPluginServer>();
            var eventService = provider.GetRequiredService<IEventService>();
            var deviceDiscoveryService = provider.GetRequiredService<IDeviceDiscoveryService>();
            var deviceServer = provider.GetRequiredService<IDeviceServer>();
            return new RealPluginManager(pluginServer, eventService, deviceDiscoveryService, deviceServer, provider.GetRequiredService<IDeviceHttpClient>());
        });

        // RealPluginManager is pre-resolved by the caller after BuildServiceProvider().

        // Phase 5: Announcement Service
        Log.Information("Registering IAnnouncementService...");
        services.AddSingleton<IAnnouncementService, AnnouncementManager>();

        // KCS File Services
        Log.Information("Registering IKcsFileService...");
        services.AddSingleton<IKcsFileService, KcsFileService>();

        // Block Script Services
        Log.Information("Registering IBlockScriptParser...");
        services.AddSingleton<IBlockScriptParser, KitX.Core.Workflow.BlockScripting.BlockScriptParser>(provider =>
        {
            var funcRegistry = provider.GetService<BuiltinFunctionRegistry>();
            var service = new KitX.Core.Workflow.BlockScripting.BlockScriptParser(funcRegistry);
            return service;
        });

        Log.Information("Registering IBlockScriptExecutor...");
        services.AddSingleton<IBlockScriptExecutor, KitX.Core.Workflow.BlockScripting.BlockScriptExecutor>(provider =>
        {
            var service = new KitX.Core.Workflow.BlockScripting.BlockScriptExecutor();
            // Resolve RealPluginManager from DI to ensure same instance
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

        Log.Information("Registering IBlockScopeManager...");
        services.AddSingleton<IBlockScopeManager, KitX.Core.Workflow.BlockScripting.BlockScopeManager>();

        // Blueprint Sub-services (must be registered before IBlueprintService)
        Log.Information("Registering Blueprint sub-services...");

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

        // Blueprint Converters
        Log.Information("Registering IBlockScriptToBlueprintConverter...");
        services.AddSingleton<IBlockScriptToBlueprintConverter>(provider =>
        {
            var parser = provider.GetRequiredService<IBlockScriptParser>();
            var nodeRegistry = provider.GetRequiredService<INodeRegistry>();
            var layoutService = provider.GetRequiredService<ILayoutService>();
            var funcRegistry = provider.GetService<BuiltinFunctionRegistry>();
            return new BlockScriptToBlueprintConverter(parser, nodeRegistry, layoutService, funcRegistry!);
        });
        Log.Information("Registering IBlueprintToBlockScriptConverter...");
        services.AddSingleton<IBlueprintToBlockScriptConverter, BlueprintToBlockScriptConverter>();

        // Blueprint Export Strategies — all auto-registered from IBuiltinFunctionDefinition implementations
        Log.Information("Registering auto-discovered builtin function export strategies...");
        foreach (var def in functionRegistry.AllDefinitions)
        {
            services.AddSingleton<INodeExportStrategy>(new BuiltinFunctionExportStrategyAdapter(def));
        }

        // Blueprint Services
        Log.Information("Registering IBlueprintService...");
        services.AddSingleton<IBlueprintService, BlueprintService>();

        // Workflow Storage Service
        Log.Information("Registering IWorkflowStorageService...");
        services.AddSingleton<IWorkflowStorageService, WorkflowStorageService>();

        // Trigger Manager
        Log.Information("Registering TriggerManager...");
        services.AddSingleton<TriggerManager>();

        // IMPORTANT: Do NOT call BuildServiceProvider() here.
        // The caller is responsible for building the single IServiceProvider and passing it
        // to ServiceHost.Initialize().

        Log.Information("AddCoreServices completed.");
        return services;
    }

}
