namespace KitX.WorkflowV6.Hosting;

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
// Registers the reflection-discovered builtin registry (32 v6 builtin functions),
// both lenses (KsTextLens + BpGraphLens), the SyncService, and the default v6
// execution backend (StructuredRoslynBackend — structured IR → structured C# via
// Roslyn, loaded into a collectible AssemblyLoadContext).
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
    /// (reflection-discovered, 32 functions across 22 source files), the two lenses
    /// (KS text + BP graph), the session sync service, and the default
    /// IExecutionBackend (StructuredRoslynBackend).
    /// </summary>
    public static IServiceCollection AddKitXWorkflowV6(this IServiceCollection services)
    {
        // BuiltinFunctionRegistry — single reflection-discovered instance. Discovers
        // the 32 v6 builtins: Print/Range/Compare/Add/Sub/Mul/Div/Mod/Len/StringConcat
        // + Pause/ReadTextFile/WriteTextFile + 7 JSON functions + 3 plugin-call
        // functions + 9 service-management functions.
        services.AddSingleton(sp =>
            BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly));

        // Lenses — bidirectional IR views. Both KsTextLens and BpGraphLens are fully
        // implemented (Parse/Project/Reverse); BpGraphLens.Diff is the only entry on
        // the deferred list (P2 milestone — see V6-BpEditAction-Future-Design-ADR.md).
        services.AddSingleton<KsTextLens>();
        services.AddSingleton<BpGraphLens>();
        services.AddSingleton<ILens<string, string>>(sp => sp.GetRequiredService<KsTextLens>());
        services.AddSingleton<ILens<Blueprint, IReadOnlyList<BpEditAction>>>(
            sp => sp.GetRequiredService<BpGraphLens>());

        // IScopeAnalyzer — walks the Blueprint's exec topology to produce sub-scope
        // regions for decorative background-frame rendering in the Dashboard BP editor.
        services.AddSingleton<IScopeAnalyzer, ScopeAnalyzer>();

        // SyncService — applies KS/BP edits to a WorkflowSession, producing a
        // WorkflowChangeSet. ApplyKsEdit is fully functional; ApplyBpEdits is
        // deferred to the P2 dual-pane-live-highlight milestone.
        services.AddSingleton<SyncService>();

        // IExecutionBackend — StructuredRoslynBackend is the default v6 backend.
        // Compiles structured IR → structured C# via Roslyn, loads into a collectible
        // AssemblyLoadContext, runs RunAsync, captures OutputLines.
        services.AddSingleton<StructuredRoslynBackend>();
        services.AddSingleton<IExecutionBackend>(sp => sp.GetRequiredService<StructuredRoslynBackend>());

        return services;
    }
}
