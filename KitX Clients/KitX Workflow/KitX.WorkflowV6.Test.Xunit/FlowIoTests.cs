// ─────────────────────────────────────────────────────────────────────────────
// Bench I/O builtin tests (FlowIn / FlowOut).
//
// The runtime methods now live on KitX.ToolKit's ToolKitExecutionGlobals (reached
// through the ToolKitExecutionGlobalsFactory), so these tests drive the real
// ToolKit stack: generated code derives from ToolKitExecutionGlobals and its
// FlowIn/FlowOut write straight to a real DataStore — no reserved-name plugin
// bridge involved.
//
// Covered:
//   • FlowIn reads a resolved trigger-binding param from the raw overrides
//     (E2E through WorkflowRunner → backend → generated code).
//   • FlowIn falls back to its default when absent / running outside a ToolKit.
//   • FlowOut writes {outputNamespace}/{key} into the DataStore; no-op without
//     a namespace.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Run;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
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
public class FlowIoTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public FlowIoTests(WorkflowTestFixture fixture) => _fixture = fixture;

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
        var services = new ServiceCollection()
            .AddSingleton(store)
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
        return new ToolKitExecutionGlobals(store, manager);
    }

    [Fact]
    public async Task FlowIn_Reads_Trigger_Param_E2E()
    {
        var (registry, lens, _, factory) = MakeStack();
        var ir = lens.Parse("FlowIn(\"userInput\", \"hi\") > Print\n", []);
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));
        var overrides = new Dictionary<string, string?> { ["userInput"] = "hello bench" };

        var result = await runner.ExecuteAsync(ir, null, overrides, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("hello bench", result.Output);
    }

    [Fact]
    public async Task FlowIn_Falls_Back_To_Default_E2E()
    {
        var (registry, lens, _, factory) = MakeStack();
        var ir = lens.Parse("FlowIn(\"missing\", \"fallback\") > Print\n", []);
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));

        // Outside a ToolKit instance there are no overrides at all.
        var result = await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("fallback", result.Output);
    }

    [Fact]
    public void FlowIn_Treats_Null_Override_As_Default()
    {
        var store = new DataStore();
        var g = MakeGlobals(store);
        g.RunContext = new HostRunContext(null, null, new Dictionary<string, string?> { ["k"] = null });
        Assert.Equal("d", g.FlowIn("k", "d"));
        Assert.Equal("", g.FlowIn("k"));
    }

    [Fact]
    public async Task FlowOut_Publishes_Through_The_Execution_Result_E2E()
    {
        var (registry, lens, store, factory) = MakeStack();
        var ir = lens.Parse("FlowOut(\"reply\", \"hi\")\n", []);
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));

        var result = await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);

        // The published value travels back with the execution result (design Q48 / §5.6.9.3):
        // it is no longer parked in the store under the node's namespace, which is what made a
        // node's output outlive the run that produced it.
        var payload = Assert.IsType<JsonElement>(result.ReturnValue);
        Assert.Equal("hi", payload.GetProperty("reply").GetString());

        // And nothing was written to the store: FlowOut has no store side effect any more.
        Assert.Empty(store.Keys());
    }

    [Fact]
    public async Task FlowOut_Publishes_Nothing_And_Stays_Silent_Without_A_Host()
    {
        var (registry, lens, store, factory) = MakeStack();
        var ir = lens.Parse("FlowOut(\"reply\", \"hi\")\n", []);
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));

        // No overrides and no run context: outside a ToolKit instance the value is simply
        // collected, never surfaced, and certainly never thrown.
        var result = await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Empty(store.Keys());
    }

    [Fact]
    public void FlowOut_Collects_Values_On_The_Instance()
    {
        var store = new DataStore();
        var g = MakeGlobals(store);
        g.RunContext = new HostRunContext(null, "ns", null);

        g.FlowOut("k", "v");
        g.FlowOut("n", 42);

        // Multiple publications accumulate on the same payload; a later key wins.
        var payload = g.PublishedPayload();
        Assert.Equal("v", payload.GetProperty("k").GetString());
        Assert.Equal(42, payload.GetProperty("n").GetInt32());

        // Two different runs never see each other's publications: the payload lives on the
        // instance, not in a process-wide store.
        var other = MakeGlobals(store);
        other.RunContext = new HostRunContext(null, "ns", null);
        Assert.False(other.PublishedPayload().TryGetProperty("k", out _));

        Assert.Empty(store.Keys());
    }

    // ── Local parse-time descriptors mirroring KitX.ToolKit's BenchFunctions. ──

    private sealed class TestBenchInFunction : IBuiltinFunction
    {
        public string Name => "FlowIn";
        public FunctionKind Kind => FunctionKind.Pure;
        public IReadOnlyList<PortSpec> InputPorts => [new("Name", PinType.String, 20), new("Default", PinType.String, 35)];
        public IReadOnlyList<PortSpec> OutputPorts => [new("Return", PinType.String, 50)];
    }

    private sealed class TestBenchOutFunction : IBuiltinFunction
    {
        public string Name => "FlowOut";
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
