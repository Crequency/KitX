// ─────────────────────────────────────────────────────────────────────────────
// Bench I/O builtin tests (BenchIn / BenchOut).
//
// The runtime methods now live on KitX.ToolKit's ToolKitExecutionGlobals (reached
// through the ToolKitExecutionGlobalsFactory), so these tests drive the real
// ToolKit stack: generated code derives from ToolKitExecutionGlobals and its
// BenchIn/BenchOut write straight to a real DataStore — no reserved-name plugin
// bridge involved.
//
// Covered:
//   • BenchIn reads a resolved trigger-binding param from the raw overrides
//     (E2E through WorkflowRunner → backend → generated code).
//   • BenchIn falls back to its default when absent / running outside a ToolKit.
//   • BenchOut writes {outputNamespace}/{key} into the DataStore; no-op without
//     a namespace.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Panels;
using KitX.ToolKit.Triggers;
using KitX.WorkflowV6.Backend;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class BenchIoTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public BenchIoTests(WorkflowTestFixture fixture) => _fixture = fixture;

    /// <summary>Builds the WorkflowV6 registry (with local parse-time Bench descriptors)
    /// plus the ToolKit execution stack the generated G derives from.</summary>
    private static (BuiltinFunctionRegistry Registry, KsTextLens Lens, DataStore Store, ToolKitExecutionGlobalsFactory Factory)
        MakeStack()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        registry.Register(new TestBenchInFunction());
        registry.Register(new TestBenchOutFunction());
        var lens = new KsTextLens(registry);

        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new NoOpExecutor(),
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));
        var runtime = new PanelRuntime(store, manager);
        var services = new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton(runtime)
            .AddSingleton(manager)
            .AddSingleton(new DataStoreOptions());
        var factory = new ToolKitExecutionGlobalsFactory(services.BuildServiceProvider());
        return (registry, lens, store, factory);
    }

    private static ToolKitExecutionGlobals MakeGlobals(DataStore store)
    {
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new NoOpExecutor(),
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));
        return new ToolKitExecutionGlobals(store, new PanelRuntime(store, manager), manager);
    }

    [Fact]
    public async Task BenchIn_Reads_Trigger_Param_E2E()
    {
        var (registry, lens, _, factory) = MakeStack();
        var ir = lens.Parse("BenchIn(\"userInput\", \"hi\") > Print\n", []);
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));
        var overrides = new Dictionary<string, string?> { ["userInput"] = "hello bench" };

        var result = await runner.ExecuteAsync(ir, null, overrides, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("hello bench", result.Output);
    }

    [Fact]
    public async Task BenchIn_Falls_Back_To_Default_E2E()
    {
        var (registry, lens, _, factory) = MakeStack();
        var ir = lens.Parse("BenchIn(\"missing\", \"fallback\") > Print\n", []);
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));

        // Outside a ToolKit instance there are no overrides at all.
        var result = await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("fallback", result.Output);
    }

    [Fact]
    public void BenchIn_Treats_Null_Override_As_Default()
    {
        var store = new DataStore();
        var g = MakeGlobals(store);
        g.RunContext = new HostRunContext(null, null, new Dictionary<string, string?> { ["k"] = null });
        Assert.Equal("d", g.BenchIn("k", "d"));
        Assert.Equal("", g.BenchIn("k"));
    }

    [Fact]
    public async Task BenchOut_Writes_Namespaced_Key_E2E()
    {
        var (registry, lens, store, factory) = MakeStack();
        var ir = lens.Parse("BenchOut(\"reply\", \"hi\")\n", []);
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));
        var overrides = new Dictionary<string, string?>
        {
            [ToolKitConstants.OutputNamespace] = "tk1/inst1/wf/wf-a",
        };

        var result = await runner.ExecuteAsync(ir, null, overrides, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("hi", store.Get("tk1/inst1/wf/wf-a/reply")?.GetString());
    }

    [Fact]
    public async Task BenchOut_NoOps_Without_Namespace_E2E()
    {
        var (registry, lens, store, factory) = MakeStack();
        var ir = lens.Parse("BenchOut(\"reply\", \"hi\")\n", []);
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));

        var result = await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Empty(store.Keys());
    }

    [Fact]
    public void BenchOut_Writes_Direct_With_Namespace()
    {
        var store = new DataStore();
        var g = MakeGlobals(store);
        g.RunContext = new HostRunContext(null, "ns", null);
        g.BenchOut("k", "v");
        Assert.Equal("v", store.Get("ns/k")?.GetString());
    }

    // ── Local parse-time descriptors mirroring KitX.ToolKit's BenchFunctions. ──

    private sealed class TestBenchInFunction : IBuiltinFunction
    {
        public string Name => "BenchIn";
        public FunctionKind Kind => FunctionKind.Pure;
        public IReadOnlyList<PortSpec> InputPorts => [new("Name", PinType.String, 20), new("Default", PinType.String, 35)];
        public IReadOnlyList<PortSpec> OutputPorts => [new("Return", PinType.String, 50)];
    }

    private sealed class TestBenchOutFunction : IBuiltinFunction
    {
        public string Name => "BenchOut";
        public FunctionKind Kind => FunctionKind.SideEffect;
        public IReadOnlyList<PortSpec> InputPorts => [new("Key", PinType.String, 20), new("Value", PinType.Any, 35)];
        public IReadOnlyList<PortSpec> OutputPorts => [];
    }

    /// <summary>A no-op executor so the manager never runs a workflow during these tests.</summary>
    private sealed class NoOpExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
