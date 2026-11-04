using KitX.ToolKit.Run;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Builtin.Functions;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Hosting;
using KitX.ToolKit.Storage;
using KitX.ToolKit.Triggers;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.ToolKit.Hosting;

/// <summary>
/// DI entry point for KitX.ToolKit. Registers the shared DataStore + its built-in plugin,
/// the trigger-source registry (with the default built-in source set), the default
/// workflow executor, the instance-model <see cref="ToolkitInstanceManager"/>, the
/// <see cref="ToolkitStore"/> and the contract services (<see cref="IToolkitService"/> /
/// <see cref="IRunService"/>).
///
/// <para>The Dashboard references this library and calls <c>AddKitXToolKit()</c> (after
/// <c>AddKitXWorkflowV6()</c>), then drives ToolKits through the contract services.</para>
///
/// <para>This is the only public type in the assembly, and <see cref="AddKitXToolKit"/> the
/// only public method — see ADR-0012 D1. Everything else is internal on purpose.</para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the KitX.ToolKit service graph.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="toolkitStorageRoot">
    /// Where ToolKit configs and workflows live. <b>Required</b> — there is deliberately no
    /// default (ADR-0010 D2). A default derived from <see cref="AppContext.BaseDirectory"/> would
    /// move with the host's deployment layout, which is unpredictable under NixOS or a service
    /// deployment; the composition root is the only place that knows where its data belongs.
    /// </param>
    public static IServiceCollection AddKitXToolKit(
        this IServiceCollection services, string toolkitStorageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolkitStorageRoot);

        // DataStore — a singleton shared data blackboard. Its scope-per-instance semantics
        // are achieved via key derivation, not separate instances.
        services.AddSingleton<DataStore>();
        services.AddSingleton<DataStoreOptions>();

        // IExecutionGlobalsFactory — the ToolKit execution-globals factory. AddKitXWorkflowV6
        // registers the default factory with TryAdd; this AddSingleton (registered later, when
        // the host calls AddKitXToolKit after AddKitXWorkflowV6) overrides it, so the generated
        // workflow G class derives from ToolKitExecutionGlobals and each run gets a fresh
        // instance wired to the DataStore and the instance manager above — replacing the
        // retired reserved-name plugin bridge (BuiltinDataStorePlugin).
        services.AddSingleton<IExecutionGlobalsFactory, ToolKitExecutionGlobalsFactory>();

        // First-class ToolKit builtin functions — registered from the descriptor registry, not
        // listed here (ADR-0011 D2). Adding a builtin means adding it to BuiltinRegistry; this
        // call site does not change, and the palette cannot advertise a function that the
        // execution globals do not implement.
        services.AddToolKitBuiltins();

        // Config validation.
        services.AddSingleton<ConfigValidator>();

        // Trigger-source registry — the default built-in source set (Manual / PluginEvent /
        // UIEvent-skeleton / WorkflowCompletion / Timer).
        services.AddSingleton(TriggerSourceRegistry.BuildDefault());

        // Shared plugin-event router — the F3 fast path. A process-wide singleton that owns the
        // single IPluginServer.PluginMessageReceived subscription and dispatches each TriggerFired
        // message to every matching PluginEventTrigger source in O(1). PluginEventTrigger.Start
        // resolves this (via IServiceProvider, so the server is resolved lazily — avoiding a DI
        // circular dependency) and falls back to a direct subscription when it is absent.
        services.AddSingleton<IPluginEventRouter, PluginEventRouter>();

        // Default workflow executor (deserializes IR + runs via WorkflowV6's WorkflowRunner,
        // which AddKitXWorkflowV6 registers — resolve lazily so registration order does not matter).
        services.AddSingleton<IWorkflowExecutor>(sp =>
            new WorkflowRunnerExecutor(sp.GetRequiredService<KitX.WorkflowV6.Services.WorkflowRunner>()));

        // Persistent ToolKit storage.
        services.AddSingleton(sp => new ToolkitStore(toolkitStorageRoot, sp.GetRequiredService<ConfigValidator>()));

        // ToolKit bundled-workflow file access (resolve / load / save / minimal template).
        services.AddSingleton<IToolkitWorkflowFileStore>(_ => new ToolkitFileStore(toolkitStorageRoot));

        // The instance-model orchestration entry point (mount / spawn / end).
        //
        // Constructed explicitly rather than by convention: ToolkitInstanceManager's constructor
        // takes the internal DataStore, so it cannot be public, and the container only reflects
        // over public constructors. Naming the arguments here is also where the composition root
        // should be — it is the one place that decides which store instance the whole ToolKit
        // shares.
        services.AddSingleton(sp => new ToolkitInstanceManager(
            sp,
            sp.GetRequiredService<TriggerSourceRegistry>(),
            sp.GetRequiredService<IWorkflowExecutor>(),
            sp.GetRequiredService<DataStore>(),
            toolkit => new ToolkitFileStore(toolkitStorageRoot),
            sp.GetRequiredService<ConfigValidator>(),
            sp.GetRequiredService<ToolkitInstanceManagerOptions>()));

        // ToolkitInstanceManagerOptions — singleton configuration for the instance
        // manager (e.g. Completed-instance retention cap). Registered with Add (last
        // wins) so a host (the Dashboard) can override the default by registering a
        // config-backed instance AFTER AddKitXToolKit().
        services.AddSingleton<ToolkitInstanceManagerOptions>();

        // Contract services — the frontend depends only on these.
        services.AddSingleton<IToolkitService, ToolkitService>();
        services.AddSingleton<IRunService, RunService>();

        return services;
    }
}
