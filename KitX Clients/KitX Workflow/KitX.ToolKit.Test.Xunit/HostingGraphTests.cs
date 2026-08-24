using KitX.ToolKit.Builtin;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Hosting;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

/// <summary>
/// Regression tests for the AddKitXToolKit / AddKitXWorkflowV6 dependency graph.
///
/// <para>History: ToolKitExecutionGlobalsFactory once took ToolkitInstanceManager (and
/// PanelRuntime, which takes the manager too) eagerly, forming a DI cycle —
/// factory → manager → IWorkflowExecutor → WorkflowRunner → StructuredRoslynBackend →
/// IExecutionGlobalsFactory → factory — that threw at FIRST resolution, which in the
/// Dashboard is the Bench page opening (ToolkitPage resolves its ViewModel on the UI
/// thread). These tests resolve the graph the same way the app does, so any reintroduced
/// cycle fails here instead of hanging the UI.</para>
/// </summary>
public class HostingGraphTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _provider;

    public HostingGraphTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kitx-hosting-graph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var services = new ServiceCollection()
            .AddKitXWorkflowV6()
            .AddKitXToolKit(_root);
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public void Full_Graph_Resolves_Without_Circular_Dependency()
    {
        // Resolving the Bench-page entry points walks the entire ToolKit/V6 chain:
        // ToolkitService → ToolkitInstanceManager → IWorkflowExecutor → BenchWorkflowRunner
        // → WorkflowRunner → StructuredRoslynBackend → IExecutionGlobalsFactory.
        // A cycle anywhere on that path throws InvalidOperationException here.
        var toolkitService = _provider.GetRequiredService<IToolkitService>();
        var benchService = _provider.GetRequiredService<IBenchService>();
        var panelRuntime = _provider.GetRequiredService<IPanelRuntime>();
        var fileStore = _provider.GetRequiredService<IToolkitWorkflowFileStore>();

        Assert.NotNull(toolkitService);
        Assert.NotNull(benchService);
        Assert.NotNull(panelRuntime);
        Assert.NotNull(fileStore);
    }

    [Fact]
    public void Factory_Resolution_Is_ToolKit_Factory_With_Lazy_BaseType()
    {
        var factory = _provider.GetRequiredService<IExecutionGlobalsFactory>();

        Assert.IsType<ToolKitExecutionGlobalsFactory>(factory);
        Assert.Equal(typeof(ToolKitExecutionGlobals), factory.BaseType);
    }

    public void Dispose()
    {
        _provider.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
