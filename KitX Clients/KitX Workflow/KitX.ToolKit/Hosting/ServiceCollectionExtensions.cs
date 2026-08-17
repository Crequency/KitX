using KitX.ToolKit.Bench;
using KitX.ToolKit.Builtin.Functions;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Panels;
using KitX.ToolKit.Services;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Triggers;
using KitX.ToolKit.Validation;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.Hosting;

/// <summary>
/// DI entry point for KitX.ToolKit. Registers the shared DataStore + its built-in plugin,
/// the trigger-source registry (with the default built-in source set), the default
/// workflow executor, the instance-model <see cref="ToolkitInstanceManager"/>, the
/// <see cref="ToolkitStore"/> and the contract services (<see cref="IToolkitService"/> /
/// <see cref="IBenchService"/>).
///
/// <para>The Dashboard references this library and calls <c>AddKitXToolKit()</c> (after
/// <c>AddKitXWorkflowV6()</c>), then drives ToolKits through the contract services.</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the KitX.ToolKit service graph.</summary>
    public static IServiceCollection AddKitXToolKit(this IServiceCollection services)
        => services.AddKitXToolKit(Path.Combine(AppContext.BaseDirectory, "Data", "Toolkits"));

    /// <summary>Registers the KitX.ToolKit service graph with an explicit ToolKit storage root.</summary>
    public static IServiceCollection AddKitXToolKit(this IServiceCollection services, string toolkitStorageRoot)
    {
        // DataStore — a singleton shared data blackboard. Its scope-per-instance semantics
        // are achieved via key derivation, not separate instances.
        services.AddSingleton<DataStore>();
        services.AddSingleton<DataStoreOptions>();
        services.AddSingleton<BuiltinDataStorePlugin>();

        // Panel runtime + its built-in plugin (KitX.UI).
        services.AddSingleton<PanelRuntime>();
        services.AddSingleton<IPanelRuntime>(sp => sp.GetRequiredService<PanelRuntime>());
        services.AddSingleton<BuiltinUiPlugin>();

        // First-class ToolKit builtin functions (Ui*/DataStore*), registered via the public
        // WorkflowV6 registration API so they appear in the BP palette and type inference.
        // Runtime execution lives on ExecutionGlobals.ToolKit (WorkflowV6) and routes through
        // the host's reserved-name bridge to the services above.
        services.AddBuiltinFunction<UiSetFunction>();
        services.AddBuiltinFunction<UiGetFunction>();
        services.AddBuiltinFunction<UiLogFunction>();
        services.AddBuiltinFunction<UiProgressFunction>();
        services.AddBuiltinFunction<UiDialogFunction>();
        services.AddBuiltinFunction<UiOpenPanelFunction>();
        services.AddBuiltinFunction<DataStoreSetFunction>();
        services.AddBuiltinFunction<DataStoreGetFunction>();
        services.AddBuiltinFunction<DataStoreWaitFunction>();
        services.AddBuiltinFunction<DataStoreWaitAnyFunction>();
        services.AddBuiltinFunction<DataStoreRemoveFunction>();
        services.AddBuiltinFunction<DataStoreKeysFunction>();
        services.AddBuiltinFunction<DataStoreContainsFunction>();
        services.AddBuiltinFunction<BenchInFunction>();
        services.AddBuiltinFunction<BenchOutFunction>();

        // Config validation.
        services.AddSingleton<ConfigValidator>();

        // Trigger-source registry — the default built-in source set (Manual / PluginEvent /
        // UIEvent-skeleton / WorkflowCompletion / Timer).
        services.AddSingleton(TriggerSourceRegistry.BuildDefault());

        // Default workflow executor (deserializes IR + runs via WorkflowV6's WorkflowRunner,
        // which AddKitXWorkflowV6 registers — resolve lazily so registration order does not matter).
        services.AddSingleton<IWorkflowExecutor>(sp =>
            new BenchWorkflowRunner(sp.GetRequiredService<KitX.WorkflowV6.Services.WorkflowRunner>()));

        // Persistent ToolKit storage.
        services.AddSingleton(sp => new ToolkitStore(toolkitStorageRoot, sp.GetRequiredService<ConfigValidator>()));

        // ToolKit bundled-workflow file access (resolve / load / save / minimal template).
        services.AddSingleton<IToolkitWorkflowFileStore>(_ => new ToolkitFileStore(toolkitStorageRoot));

        // The instance-model orchestration entry point (mount / spawn / end).
        services.AddSingleton<ToolkitInstanceManager>();

        // Contract services — the frontend depends only on these.
        services.AddSingleton<IToolkitService, ToolkitService>();
        services.AddSingleton<IBenchService, BenchService>();

        return services;
    }
}
