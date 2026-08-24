// ─────────────────────────────────────────────────────────────────────────────
// WorkflowV6 host-extension tests.
//
// Covers the WorkflowV6 side of the ToolKit builtin integration:
//   • WorkflowRunner extracts the instance id / output namespace / raw overrides
//     from the constant overrides and hands them to the backend as a HostRunContext.
//   • The public registration API (AddBuiltinFunction<T>) folds a DI-constructed
//     function into the shared BuiltinFunctionRegistry singleton.
//   • PluginNotify routes to the host without using the blocking Call path.
//
// The Ui*/DataStore*/Bench* builtin dispatch is no longer routed by reserved plugin
// name — those methods moved onto KitX.ToolKit's ToolKitExecutionGlobals (covered by
// the KitX.ToolKit.Test.Xunit project), so their per-instance behavior is tested there.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Hosting;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Ir.Lowering;
using KitX.WorkflowV6.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class ToolKitBuiltinDispatchTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public ToolKitBuiltinDispatchTests(WorkflowTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Runner_Extracts_InstanceId_From_Overrides()
    {
        var ir = _fixture.ParseKS("Print(\"x\")\n");
        var backend = new RecordingBackend();
        var runner = new WorkflowRunner(backend);

        await runner.ExecuteAsync(
            ir, null,
            new Dictionary<string, string?> { [ToolKitConstants.InstanceId] = "inst-9" },
            CancellationToken.None);

        Assert.Equal("inst-9", backend.LastContext?.InstanceId);
    }

    [Fact]
    public async Task Runner_Passes_Null_InstanceId_When_Absent()
    {
        var ir = _fixture.ParseKS("Print(\"x\")\n");
        var backend = new RecordingBackend();
        var runner = new WorkflowRunner(backend);

        await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        // The context is always handed over (raw overrides feed BenchIn), but the
        // instance-scoped fields degrade to null outside a ToolKit instance.
        Assert.NotNull(backend.LastContext);
        Assert.Null(backend.LastContext.InstanceId);
        Assert.Null(backend.LastContext.OutputNamespace);
        Assert.Null(backend.LastContext.RawOverrides);
    }

    [Fact]
    public async Task Runner_Extracts_Bench_Context_From_Overrides()
    {
        var ir = _fixture.ParseKS("Print(\"x\")\n");
        var backend = new RecordingBackend();
        var runner = new WorkflowRunner(backend);
        var overrides = new Dictionary<string, string?>
        {
            [ToolKitConstants.InstanceId] = "inst-9",
            [ToolKitConstants.OutputNamespace] = "tk/inst-9/wf/wf-a",
            ["userInput"] = "hello",
        };

        await runner.ExecuteAsync(ir, null, overrides, CancellationToken.None);

        Assert.Equal("inst-9", backend.LastContext!.InstanceId);
        Assert.Equal("tk/inst-9/wf/wf-a", backend.LastContext.OutputNamespace);
        Assert.Same(overrides, backend.LastContext.RawOverrides);
    }

    [Fact]
    public void AddBuiltinFunction_Registers_Into_Shared_Registry()
    {
        var services = new ServiceCollection();
        services.AddKitXWorkflowV6();
        services.AddBuiltinFunction<TestBuiltinFunction>();
        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<BuiltinFunctionRegistry>();
        Assert.True(registry.Contains("TestBuiltin"));
        Assert.Equal(FunctionKind.Pure, registry.Get("TestBuiltin")!.Kind);
    }

    [Fact]
    public void PluginNotify_Routes_To_HostNotify_Without_Using_Call()
    {
        var g = new ExecutionGlobals { PluginHost = new RecordingHost() };
        g.PluginNotify("TestPlugin", "ShowPopup", "hello");

        var host = (RecordingHost)g.PluginHost!;
        var notify = Assert.Single(host.NotifyCalls);
        Assert.Equal("TestPlugin", notify.Plugin);
        Assert.Equal("ShowPopup", notify.Method);
        Assert.Equal(new object[] { "hello" }, notify.Args);
        Assert.Empty(host.Calls); // must not fall back to the blocking Call path
    }

    [Fact]
    public void PluginNotify_Is_Discovered_As_Builtin()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        Assert.True(registry.Contains("PluginNotify"));
        Assert.Empty(registry.Get("PluginNotify")!.OutputPorts);
    }

    /// <summary>A DI-constructed test builtin (no parameterless ctor) used to verify the
    /// public registration API folds it into the shared registry.</summary>
    private sealed class TestBuiltinFunction : IBuiltinFunction
    {
        public string Name => "TestBuiltin";
        public FunctionKind Kind => FunctionKind.Pure;
        public IReadOnlyList<PortSpec> InputPorts => [];
        public IReadOnlyList<PortSpec> OutputPorts => [];
    }

    private sealed class RecordingHost : IPluginHost
    {
        public List<(string Plugin, string Method, object?[] Args)> Calls { get; } = [];
        public List<(string Plugin, string Method, object?[] Args)> NotifyCalls { get; } = [];

        public object? Call(string pluginName, string methodName, params object[] args)
        {
            Calls.Add((pluginName, methodName, args));
            return true;
        }

        public void Notify(string pluginName, string methodName, params object[] args)
        {
            NotifyCalls.Add((pluginName, methodName, args));
        }

        public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args) => null;
        public object? TryGetDevice(string deviceName) => null;
        public bool StartPlugin(string pluginName) => true;
        public bool StopPlugin(string pluginName) => true;
        public bool InstallPlugin(string kxpPath) => true;
        public string GetPluginInfoByName(string pluginName) => string.Empty;
        public string ListPluginNames() => "[]";
    }

    private sealed class RecordingBackend : IExecutionBackend
    {
        public HostRunContext? LastContext { get; private set; }
        public string Name => "Recording";

        public Task<BlockScriptExecutionResult> ExecuteAsync(
            Workflow ir, LoweringResult? lowering, CancellationToken ct, HostRunContext? hostContext = null)
        {
            LastContext = hostContext;
            return Task.FromResult(new BlockScriptExecutionResult { IsSuccess = true });
        }

        public Task<BlockScriptExecutionResult> ExecuteAsync(
            Workflow ir, LoweringResult? lowering, CancellationToken ct,
            IBlueprintDebugController? debugger, HostRunContext? hostContext = null)
            => ExecuteAsync(ir, lowering, ct, hostContext);
    }
}
