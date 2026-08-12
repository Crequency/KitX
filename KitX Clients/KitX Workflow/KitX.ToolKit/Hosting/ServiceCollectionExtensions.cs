using KitX.ToolKit.Bench;
using KitX.ToolKit.Data;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.Hosting;

/// <summary>
/// DI entry point for KitX.ToolKit. Registers the shared DataStore + its built-in plugin,
/// the trigger-source registry (with the default built-in source set), the default
/// workflow executor and the unified <see cref="BenchTriggerManager"/>.
///
/// <para>The Dashboard references this library and calls <c>AddKitXToolKit()</c> (after
/// <c>AddKitXWorkflowV6()</c>), then calls <see cref="BenchTriggerManager.Activate"/> with a
/// parsed <see cref="Models.Toolkit"/> config to run a ToolKit.</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the KitX.ToolKit service graph.</summary>
    public static IServiceCollection AddKitXToolKit(this IServiceCollection services)
    {
        // DataStore — a singleton shared data blackboard. Its scope-per-instance semantics
        // are achieved via key derivation, not separate instances.
        services.AddSingleton<DataStore>();
        services.AddSingleton<DataStoreOptions>();
        services.AddSingleton<BuiltinDataStorePlugin>();

        // Config validation.
        services.AddSingleton<ConfigValidator>();

        // Trigger-source registry — the default built-in source set (Manual / PluginEvent /
        // UIEvent-skeleton / WorkflowCompletion / Timer).
        services.AddSingleton(TriggerSourceRegistry.BuildDefault());

        // Default workflow executor (deserializes IR + runs via WorkflowV6's WorkflowRunner,
        // which AddKitXWorkflowV6 registers — resolve lazily so registration order does not matter).
        services.AddSingleton<IWorkflowExecutor>(sp =>
            new BenchWorkflowRunner(sp.GetRequiredService<KitX.WorkflowV6.Services.WorkflowRunner>()));

        // The unified trigger dispatcher / orchestration entry point.
        services.AddSingleton<BenchTriggerManager>();

        return services;
    }
}
