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
/// Extension methods for configuring KitX Core services in the DI container
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
        services.AddSingleton<IConfigService>(ConfigManager.Instance);

        // Security Services
        Log.Information("Registering IDeviceKeyService and IEncryptionService...");
        services.AddSingleton<IDeviceKeyService>(SecurityManager.Instance);
        services.AddSingleton<IEncryptionService>(SecurityManager.Instance);

        // Plugin Services
        Log.Information("Registering IPluginService...");
        services.AddSingleton<IPluginService>(PluginsManager.Instance);

        // Workflow Services
        Log.Information("Registering workflow services...");
        // Individual service implementations (WorkflowScriptService facade used for backward compatibility)
        services.AddSingleton<IBlockScriptService>(sp => WorkflowScriptService.BlockScriptServiceInstance);
        services.AddSingleton<IWorkflowPluginService>(sp => WorkflowScriptService.PluginServiceInstance);
        services.AddSingleton<IScriptExecutionService>(sp => WorkflowScriptService.ScriptExecutionServiceInstance);
        services.AddSingleton<IWorkflowManagementService>(sp => WorkflowScriptService.ManagementServiceInstance);

        // Activity Services
        Log.Information("Registering IActivityService...");
        services.AddSingleton<IActivityService>(ActivityManager.Instance);

        // Statistics Services
        Log.Information("Registering IStatisticsService...");
        services.AddSingleton<IStatisticsService>(StatisticsManager.Instance);

        // Task Services
        Log.Information("Registering ITasksService...");
        services.AddSingleton<ITasksService>(TasksManager.Instance);

        // File Watcher Services
        Log.Information("Registering IFileWatcherService...");
        services.AddSingleton<IFileWatcherService>(FileWatcherManager.Instance);

        // Hotkey Services
        Log.Information("Registering IKeyHookService...");
        services.AddSingleton<IKeyHookService>(KeyHookManager.Instance);

        // Event Services
        Log.Information("Registering IEventService...");
        services.AddSingleton<IEventService>(EventService.Instance);

        // Phase 5: Device and Network Services
        Log.Information("Registering IDeviceDiscoveryService...");
        services.AddSingleton<IDeviceDiscoveryService>(DevicesDiscoveryServer.Instance);

        Log.Information("Registering IDeviceServer...");
        services.AddSingleton<IDeviceServer>(DevicesServer.Instance);

        Log.Information("Registering IPluginServer...");
        services.AddSingleton<IPluginServer>(PluginsServer.Instance);

        // Phase 5: Device HTTP Client (for cross-device plugin invocation)
        Log.Information("Registering IDeviceHttpClient...");
        services.AddSingleton<IDeviceHttpClient, DeviceHttpClient>();

        // Phase 5: Announcement Service
        Log.Information("Registering IAnnouncementService...");
        services.AddSingleton<IAnnouncementService>(provider =>
        {
            var configService = provider.GetService<IConfigService>();
            var service = configService != null
                ? new AnnouncementManager(configService)
                : AnnouncementManager.Instance;
            return service;
        });

        // KCS File Services
        Log.Information("Registering IKcsFileService...");
        services.AddSingleton<IKcsFileService, KcsFileService>(provider =>
        {
            var service = new KcsFileService();
            return service;
        });

        // Main Program Analyzer
        Log.Information("Registering IMainProgramAnalyzer...");
        services.AddSingleton<IMainProgramAnalyzer, MainProgramAnalyzer>(provider =>
        {
            var service = new MainProgramAnalyzer();
            return service;
        });

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
            // Wire up plugin manager if PluginsServer is available
            try
            {
                var pluginManager = new KitX.Core.Workflow.RealPluginManager(
                    KitX.Core.Device.PluginsServer.Instance);
                service.SetPluginManager(pluginManager);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not initialize BlockScriptExecutor with plugin manager");
            }
            return service;
        });

        Log.Information("Registering IBlockScopeManager...");
        services.AddSingleton<IBlockScopeManager, KitX.Core.Workflow.BlockScripting.BlockScopeManager>(provider =>
        {
            var service = new KitX.Core.Workflow.BlockScripting.BlockScopeManager();
            return service;
        });

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
        services.AddSingleton<IWorkflowStorageService>(WorkflowStorageService.Instance);

        // Trigger Manager
        Log.Information("Registering TriggerManager...");
        services.AddSingleton<TriggerManager>(TriggerManager.Instance);

        Log.Information("AddCoreServices completed.");
        return services;
    }
}
