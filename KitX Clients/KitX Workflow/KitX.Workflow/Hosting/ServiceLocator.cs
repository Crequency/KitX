using Microsoft.Extensions.DependencyInjection;

namespace KitX.Workflow.Hosting;

/// <summary>
/// Centralized service locator for the KitX.Workflow library.
///
/// This is the workflow library's own counterpart of KitX.Core's ServiceLocator.
/// The host application (KitX.Core) must call <see cref="Initialize"/> with the
/// single shared <see cref="IServiceProvider"/> after building it, so that workflow
/// code created outside of DI (e.g. builtin-function runtime methods, lazy singletons)
/// can resolve shared services (IPluginService, IDeviceServer, workflow services, ...)
/// through the same container, without taking a compile-time dependency on KitX.Core.
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _serviceProvider;
    private static bool _isInitialized;

    /// <summary>
    /// Gets the single IServiceProvider for the workflow library.
    /// Throws if accessed before initialization.
    /// </summary>
    public static IServiceProvider ServiceProvider
    {
        get
        {
            if (_serviceProvider is null)
                throw new InvalidOperationException(
                    "ServiceLocator has not been initialized. " +
                    "Call ServiceLocator.Initialize() first.");

            return _serviceProvider;
        }
    }

    /// <summary>Gets whether the locator has been initialized.</summary>
    public static bool IsInitialized => _isInitialized;

    /// <summary>
    /// Initializes the locator with the single shared IServiceProvider.
    /// Should be called exactly ONCE during application startup,
    /// after building the service provider.
    /// </summary>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        if (_isInitialized)
        {
            Serilog.Log.Warning("[ServiceLocator] Initialize called more than once. Ignoring.");
            return;
        }

        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _isInitialized = true;

        Serilog.Log.Information("[ServiceLocator] Initialized. ServiceProvider HashCode: {HashCode}",
            serviceProvider.GetHashCode());
    }

    /// <summary>
    /// Gets a required service from the DI container.
    /// Throws InvalidOperationException if the service is not registered.
    /// </summary>
    public static T GetRequiredService<T>() where T : notnull
        => ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// Gets a service from the DI container, or null if not registered.
    /// </summary>
    public static T? GetService<T>() where T : class
        => ServiceProvider.GetService<T>();
}
