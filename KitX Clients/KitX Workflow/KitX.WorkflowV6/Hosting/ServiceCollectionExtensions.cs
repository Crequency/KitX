namespace KitX.WorkflowV6.Hosting;

using System.Reflection;
using KitX.WorkflowV6.Backend;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Session;
using Microsoft.Extensions.DependencyInjection;

// ─────────────────────────────────────────────────────────────────────────────
// ServiceCollectionExtensions — DI entry point for KitX.WorkflowV6.
//
// Registers the reflection-discovered builtin registry (41 v6 builtin functions),
// both lenses (KsTextLens + BpGraphLens), the SyncService, the default v6
// execution backend (StructuredRoslynBackend — structured IR → structured C# via
// Roslyn, loaded into a collectible AssemblyLoadContext), and the workflow
// services (WorkflowStorageService / WorkflowSessionManager).
//
// The Dashboard references this library (KitX.Dashboard.csproj ProjectReference)
// and calls AddKitXWorkflowV6() in App.axaml.cs. Since the v5.1 WorkflowIR library
// was archived (Package/Archive), the v6 registrations are the only workflow
// pipeline — shared interface names (ILens<>, IExecutionBackend) resolve to v6.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// DI registration extensions for the KitX.WorkflowV6 library.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the KitX.WorkflowV6 service graph: the builtin-function registry
    /// (reflection-discovered, 41 functions across 25 source files), the two lenses
    /// (KS text + BP graph), the session sync service, the default
    /// IExecutionBackend (StructuredRoslynBackend), and the workflow services
    /// (IWorkflowStorageService / IWorkflowManagementService).
    /// </summary>
    public static IServiceCollection AddKitXWorkflowV6(this IServiceCollection services)
    {
        // BuiltinFunctionRegistry — single reflection-discovered instance. Discovers
        // the 41 v6 builtins: Print/Range/Compare/Add/Sub/Mul/Div/Mod/Len/StringConcat
        // + Pause/ReadTextFile/WriteTextFile + 7 JSON functions + 9 dict functions
        // + 3 plugin-call functions + 9 service-management functions.
        //
        // The registry is a DI singleton so other KitX systems can extend it: any
        // IBuiltinFunction registered via AddBuiltinFunction<T>() / AddBuiltinFunctions()
        // is folded into the same registry on first resolution. This is the public
        // extension seam for host-side builtins (e.g. KitX.ToolKit's Ui*/DataStore*).
        services.AddSingleton(sp =>
        {
            var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
            foreach (var fn in sp.GetServices<IBuiltinFunction>())
                registry.Register(fn);
            return registry;
        });

        // Lenses — bidirectional IR views. Both KsTextLens and BpGraphLens are fully
        // implemented (Parse/Project/Reverse); BpGraphLens.Diff is the only entry on
        // the deferred list (P2 milestone — see V6-BpEditAction-Future-Design-ADR.md).
        services.AddSingleton<KsTextLens>();
        services.AddSingleton<BpGraphLens>();
        services.AddSingleton<ILens<string, string>>(sp => sp.GetRequiredService<KsTextLens>());
        services.AddSingleton<ILens<Blueprint, IReadOnlyList<BpEditAction>>>(
            sp => sp.GetRequiredService<BpGraphLens>());

        // SyncService — applies KS/BP edits to a WorkflowSession, producing a
        // WorkflowChangeSet. ApplyKsEdit is fully functional; ApplyBpEdits is
        // deferred to the P2 dual-pane-live-highlight milestone.
        services.AddSingleton<SyncService>();

        // IExecutionBackend — StructuredRoslynBackend is the default v6 backend.
        // Compiles structured IR → structured C# via Roslyn, loads into a collectible
        // AssemblyLoadContext, runs RunAsync, captures OutputLines.
        services.AddSingleton<StructuredRoslynBackend>();
        services.AddSingleton<IExecutionBackend>(sp => sp.GetRequiredService<StructuredRoslynBackend>());

        // WorkflowRunner — single shared execution path (ApplyConstantOverrides +
        // ExecuteAsync) used by the editor Run/DebugRun and by WorkflowSessionManager's
        // run-by-id path. Registered as both its concrete type (for same-library
        // consumers) and its IWorkflowRunner abstraction (for cross-library
        // interface-based consumers such as the Dashboard editor), sharing one
        // singleton instance.
        services.AddSingleton<Services.WorkflowRunner>();
        services.AddSingleton<Services.IWorkflowRunner>(sp => sp.GetRequiredService<Services.WorkflowRunner>());

        // Workflow services (migrated from KitX.Dashboard.Services — zero UI deps):
        //   • WorkflowStorageService — file-based IWorkflowStorageService for KcsFileFormat v2.
        //   • WorkflowSessionManager — IWorkflowManagementService run/stop-by-id orchestrator
        //     (loads stored IR, applies VariableConstants overrides, executes via the backend).
        services.AddSingleton<KitX.Core.Contract.Workflow.IWorkflowStorageService,
            KitX.WorkflowV6.Services.WorkflowStorageService>();
        services.AddSingleton<KitX.Core.Contract.Workflow.IWorkflowManagementService,
            KitX.WorkflowV6.Services.WorkflowSessionManager>();

        return services;
    }

    /// <summary>
    /// Registers a builtin function, constructed via DI (no parameterless-ctor reflection
    /// requirement). The function is folded into the shared <see cref="BuiltinFunctionRegistry"/>
    /// singleton on first resolution, so it appears in the BP palette and type inference.
    /// This is the public extension seam for host-side builtins (e.g. KitX.ToolKit's
    /// Ui*/DataStore* families) and any future KitX system.
    /// </summary>
    public static IServiceCollection AddBuiltinFunction<T>(this IServiceCollection services)
        where T : class, IBuiltinFunction
    {
        services.AddSingleton<IBuiltinFunction, T>();
        return services;
    }

    /// <summary>
    /// Registers every concrete <see cref="IBuiltinFunction"/> in an assembly via reflection
    /// (parameterless-ctor requirement, like <see cref="BuiltinFunctionRegistry.Discover"/>).
    /// Prefer <see cref="AddBuiltinFunction{T}"/> for DI-constructed functions.
    /// </summary>
    public static IServiceCollection AddBuiltinFunctions(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (!typeof(IBuiltinFunction).IsAssignableFrom(type)) continue;
            if (type.IsAbstract || type.IsInterface) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;
            services.AddSingleton(typeof(IBuiltinFunction), type);
        }
        return services;
    }
}
