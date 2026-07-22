namespace KitX.WorkflowV6.Hosting;

using KitX.WorkflowV6.Backend;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Lens;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Session;
using Microsoft.Extensions.DependencyInjection;

// ─────────────────────────────────────────────────────────────────────────────
// ServiceCollectionExtensions — DI entry point for KitX.WorkflowV6.
//
// Inherited shape from KitX.WorkflowIR.Hosting.ServiceCollectionExtensions.AddKitXWorkflowIR,
// re-aimed at the v6 types: registers the (currently empty) builtin registry, the
// two lenses (placeholder bodies), the SyncService, and an IExecutionBackend slot.
//
// The Dashboard does NOT reference this library yet (decision: experiment-period
// isolation). The Dashboard's existing AddKitXWorkflowIR() call keeps the v5 pipeline
// running; when the v6 implementation is far enough along, the Dashboard will add a
// parallel AddKitXWorkflowV6() call and select between the two via feature flag.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// DI registration extensions for the KitX.WorkflowV6 library.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the KitX.WorkflowV6 service graph: the builtin-function registry
    /// (reflection-discovered, currently empty), the two lenses (KS text + BP graph),
    /// the session sync service, and an IExecutionBackend slot (no default impl yet).
    /// </summary>
    public static IServiceCollection AddKitXWorkflowV6(this IServiceCollection services)
    {
        // BuiltinFunctionRegistry — single reflection-discovered instance. The v6
        // library currently ships no builtins, so this returns an empty registry.
        services.AddSingleton(sp =>
            BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly));

        // Lenses — bidirectional IR views. Placeholder bodies; signatures are stable.
        services.AddSingleton<KsTextLens>();
        services.AddSingleton<BpGraphLens>();
        services.AddSingleton<ILens<string, string>>(sp => sp.GetRequiredService<KsTextLens>());
        services.AddSingleton<ILens<Blueprint, IReadOnlyList<BpEditAction>>>(
            sp => sp.GetRequiredService<BpGraphLens>());

        // SyncService — applies KS/BP edits to a WorkflowSession, producing a
        // WorkflowChangeSet.
        services.AddSingleton<SyncService>();

        // IExecutionBackend — no default implementation yet. The structured-C# Roslyn
        // backend ships with the implementation plan; until then, callers that need
        // an execution backend must register their own (or use the v5 backend via
        // KitX.WorkflowIR).
        // services.AddSingleton<IExecutionBackend, StructuredRoslynBackend>();

        return services;
    }
}
