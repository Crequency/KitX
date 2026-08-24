using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace KitX.Core.DI;

/// <summary>
/// 统一动态服务注册管理中心，持有唯一 IServiceProvider。
/// 所有服务解析通过此类进行，确保单例一致性。
/// </summary>
public static class ServiceHost
{
    private static IServiceProvider? _serviceProvider;
    private static bool _isInitialized;

    /// <summary>
    /// Gets the single IServiceProvider for the application.
    /// Throws if accessed before initialization.
    /// </summary>
    public static IServiceProvider ServiceProvider
    {
        get
        {
            if (_serviceProvider == null)
                throw new InvalidOperationException(
                    "ServiceHost has not been initialized. " +
                    "Call ServiceHost.Initialize() first.");

            return _serviceProvider;
        }
    }

    /// <summary>
    /// Gets whether the ServiceHost has been initialized.
    /// </summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Initializes the ServiceHost with the single IServiceProvider.
    /// This should be called exactly ONCE during application startup,
    /// after building the service provider.
    /// </summary>
    /// <param name="serviceProvider">The single IServiceProvider instance</param>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        if (_isInitialized)
        {
            Log.Warning("[ServiceHost] Initialize called more than once. Ignoring.");
            return;
        }

        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _isInitialized = true;

        Log.Information("[ServiceHost] Initialized. ServiceProvider HashCode: {HashCode}",
            serviceProvider.GetHashCode());
    }

    /// <summary>
    /// Gets a required service from the DI container.
    /// Throws InvalidOperationException if the service is not registered.
    /// Use this for all core services that MUST be registered.
    /// </summary>
    public static T GetRequiredService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Gets a service from the DI container, or null if not registered.
    /// Use this for optional services.
    /// </summary>
    public static T? GetService<T>() where T : class
    {
        return ServiceProvider.GetService<T>();
    }

    /// <summary>
    /// Creates an instance of an unregistered type using constructor injection
    /// from the DI container. Use this for ViewModels and other types that
    /// are not explicitly registered but have constructor dependencies on
    /// registered services.
    /// </summary>
    public static T CreateInstance<T>() where T : class
    {
        return ActivatorUtilities.CreateInstance<T>(ServiceProvider);
    }
}