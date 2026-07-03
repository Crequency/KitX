using KitX.Workflow.Backend;
using KitX.Workflow.Backend.RoslynBackend;
using KitX.Workflow.Backend.Runtime;
using KitX.Workflow.Builtin;
using KitX.Workflow.Lens;
using KitX.Workflow.Lens.BpGraphLens;
using KitX.Workflow.Lens.BsTextLens;
using KitX.Workflow.Session;
using Microsoft.Extensions.DependencyInjection;

namespace KitX.Workflow.Hosting;

// ─────────────────────────────────────────────────────────────────────────────
// ServiceCollectionExtensions — DI entry point for the KitX.Workflow library.
//
// This is the successor to the legacy KitX.Workflow.Hosting.ServiceCollectionExtensions
// .AddKitXWorkflow() registration. The new library's service graph is far smaller
// because the mutable CFG pipeline (BlockScriptService, BlockScriptExecutor,
// CfgGraphRenderer, CfgDiffer, NodeRegistry, LayoutService, TriggerManager, ...) is
// gone: the IR + Lens + Diff + Roslyn backend is the entire execution path.
//
// Host wiring (post Phase 12-prep): KitX.Core no longer registers workflow services.
// A host that wants the new IR pipeline calls AddKitXWorkflowIR() on its service
// collection. The Dashboard front-end migration (separate work) will consume these
// registrations to replace the stubbed-out legacy entry points.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// DI registration extensions for the KitX.Workflow library.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the KitX.Workflow service graph: the builtin-function registry
    /// (reflection-discovered), the two lenses (BS text + BP graph), the session
    /// sync service, and the default Roslyn execution backend.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKitXWorkflowIR(this IServiceCollection services)
    {
        // BuiltinFunctionRegistry — single reflection-discovered instance shared by
        // both lenses, the sync service, and the execution backend.
        services.AddSingleton(sp =>
            BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly));

        // Lenses — bidirectional IR views. Each takes the (optional) registry so it can
        // resolve builtin-function render/reverse handlers during Project/Diff.
        services.AddSingleton<BsTextLens>();
        services.AddSingleton<BpGraphLens>();
        services.AddSingleton<ILens<string, string>>(sp => sp.GetRequiredService<BsTextLens>());
        services.AddSingleton<ILens<Blueprint, IReadOnlyList<BpEditAction>>>(
            sp => sp.GetRequiredService<BpGraphLens>());

        // SyncService — applies BS/BP edits to a WorkflowSession, producing an IrChangeSet.
        services.AddSingleton<SyncService>();

        // Execution backend — the default Roslyn compile+load+run implementation.
        // IPluginHost is optional; register null unless the host provides one.
        services.AddSingleton<IExecutionBackend>(sp =>
        {
            var registry = sp.GetRequiredService<BuiltinFunctionRegistry>();
            var pluginHost = sp.GetService<IPluginHost>();
            return new RoslynExecutionBackend(registry, pluginHost);
        });

        return services;
    }
}
