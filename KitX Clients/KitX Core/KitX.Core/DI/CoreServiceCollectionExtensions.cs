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
using KitX.Core.Event;
using KitX.Workflow.Hosting;
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

        // Phase 5: Announcement Service
        Log.Information("Registering IAnnouncementService...");
        services.AddSingleton<IAnnouncementService, AnnouncementManager>();

        // Workflow Services — full graph registered by the KitX.Workflow library.
        // This includes RealPluginManager (registered as a singleton so PluginsServer and
        // WorkflowScriptService share the same instance and receive plugin connection events;
        // the caller pre-resolves it after BuildServiceProvider()).
        services.AddKitXWorkflow();

        // IMPORTANT: Do NOT call BuildServiceProvider() here.
        // The caller is responsible for building the single IServiceProvider and passing it
        // to ServiceHost.Initialize().

        Log.Information("AddCoreServices completed.");
        return services;
    }

}
