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
using KitX.Core.Task;
using KitX.Core.Workflow;
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
        services.AddSingleton<IConfigService, ConfigManager>(provider =>
        {
            var service = ConfigManager.Instance;
            return service;
        });

        // Security Services
        Log.Information("Registering ISecurityService...");
        services.AddSingleton<ISecurityService, SecurityManager>(provider =>
        {
            var service = SecurityManager.Instance;
            return service;
        });

        // Plugin Services
        Log.Information("Registering IPluginService...");
        services.AddSingleton<IPluginService, PluginsManager>(provider =>
        {
            var service = PluginsManager.Instance;
            return service;
        });

        // Workflow Services
        Log.Information("Registering IWorkflowService...");
        services.AddSingleton<IWorkflowService, WorkflowScriptService>(provider =>
        {
            var service = WorkflowScriptService.Instance;
            return service;
        });

        // Activity Services
        Log.Information("Registering IActivityService...");
        services.AddSingleton<IActivityService, ActivityManager>(provider =>
        {
            var service = ActivityManager.Instance;
            return service;
        });

        // Statistics Services
        Log.Information("Registering IStatisticsService...");
        services.AddSingleton<IStatisticsService, StatisticsManager>(provider =>
        {
            var service = StatisticsManager.Instance;
            return service;
        });

        // Task Services
        Log.Information("Registering ITasksService...");
        services.AddSingleton<ITasksService, TasksManager>(provider =>
        {
            var service = TasksManager.Instance;
            return service;
        });

        // File Watcher Services
        Log.Information("Registering IFileWatcherService...");
        services.AddSingleton<IFileWatcherService, FileWatcherManager>(provider =>
        {
            var service = FileWatcherManager.Instance;
            return service;
        });

        // Hotkey Services
        Log.Information("Registering IKeyHookService...");
        services.AddSingleton<IKeyHookService, KeyHookManager>(provider =>
        {
            var service = KeyHookManager.Instance;
            return service;
        });

        // Event Services
        Log.Information("Registering IEventService...");
        services.AddSingleton<IEventService, EventService>(provider =>
        {
            var service = EventService.Instance;
            return service;
        });

        // Phase 5: Device and Network Services
        Log.Information("Registering IDeviceDiscoveryService...");
        services.AddSingleton<IDeviceDiscoveryService, DevicesDiscoveryServer>(provider =>
        {
            var service = DevicesDiscoveryServer.Instance;
            return service;
        });

        Log.Information("Registering IDeviceServer...");
        services.AddSingleton<IDeviceServer, DevicesServer>(provider =>
        {
            var service = DevicesServer.Instance;
            return service;
        });

        Log.Information("Registering IPluginServer...");
        services.AddSingleton<IPluginServer, PluginsServer>(provider =>
        {
            var service = PluginsServer.Instance;
            return service;
        });

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

        Log.Information("AddCoreServices completed.");
        return services;
    }
}
