// ─────────────────────────────────────────────────────────────────────────────
// Bench I/O builtin tests (BenchIn / BenchOut).
//
// The descriptors live in KitX.ToolKit (which references WorkflowV6, so they
// cannot be referenced from here); parse-time descriptors with identical shape
// are declared locally, while the runtime methods under test are the real
// ExecutionGlobals.Bench pair invoked through generated code.
//
// Covered:
//   • BenchIn reads a resolved trigger-binding param from the raw overrides
//     (E2E through WorkflowRunner → backend → generated code).
//   • BenchIn falls back to its default when absent / running outside a ToolKit.
//   • BenchOut writes {outputNamespace}/{key} via the KitX.DataStore reserved
//     bridge; no-op without a namespace or host.
// ─────────────────────────────────────────────────────────────────────────────

using KitX.Core.Contract.Workflow;
using KitX.WorkflowV6.Backend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Services;
using Xunit;

namespace KitX.WorkflowV6.Test.Xunit;

[Trait("Category", "Integration")]
public class BenchIoTests : IClassFixture<WorkflowTestFixture>
{
    private readonly WorkflowTestFixture _fixture;
    public BenchIoTests(WorkflowTestFixture fixture) => _fixture = fixture;

    private KsTextLens MakeLens()
    {
        var registry = BuiltinFunctionRegistry.Discover(typeof(BuiltinFunctionRegistry).Assembly);
        registry.Register(new TestBenchInFunction());
        registry.Register(new TestBenchOutFunction());
        return new KsTextLens(registry);
    }

    [Fact]
    public async Task BenchIn_Reads_Trigger_Param_E2E()
    {
        var ir = MakeLens().Parse("BenchIn(\"userInput\", \"none\") > Print\n", []);
        var host = new RecordingHost();
        var runner = new WorkflowRunner(_fixture.MakeBackend(host));
        var overrides = new Dictionary<string, string?> { ["userInput"] = "hello bench" };

        var result = await runner.ExecuteAsync(ir, null, overrides, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("hello bench", result.Output);
    }

    [Fact]
    public async Task BenchIn_Falls_Back_To_Default_E2E()
    {
        var ir = MakeLens().Parse("BenchIn(\"missing\", \"fallback\") > Print\n", []);
        var runner = new WorkflowRunner(_fixture.MakeBackend(new RecordingHost()));

        // Outside a ToolKit instance there are no overrides at all.
        var result = await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("fallback", result.Output);
    }

    [Fact]
    public void BenchIn_Treats_Null_Override_As_Default()
    {
        var g = new ExecutionGlobals
        {
            RawOverrides = new Dictionary<string, string?> { ["k"] = null },
        };
        Assert.Equal("d", g.BenchIn("k", "d"));
        Assert.Equal("", g.BenchIn("k"));
    }

    [Fact]
    public async Task BenchOut_Writes_Namespaced_Key_E2E()
    {
        var ir = MakeLens().Parse("BenchOut(\"reply\", \"hi\")\n", []);
        var host = new RecordingHost();
        var runner = new WorkflowRunner(_fixture.MakeBackend(host));
        var overrides = new Dictionary<string, string?>
        {
            [ToolKitConstants.OutputNamespace] = "tk1/inst1/wf/wf-a",
        };

        var result = await runner.ExecuteAsync(ir, null, overrides, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var call = Assert.Single(host.Calls);
        Assert.Equal("KitX.DataStore", call.Plugin);
        Assert.Equal("Set", call.Method);
        Assert.Equal(new object[] { "tk1/inst1/wf/wf-a/reply", "hi" }, call.Args);
    }

    [Fact]
    public async Task BenchOut_NoOps_Without_Namespace_E2E()
    {
        var ir = MakeLens().Parse("BenchOut(\"reply\", \"hi\")\n", []);
        var host = new RecordingHost();
        var runner = new WorkflowRunner(_fixture.MakeBackend(host));

        var result = await runner.ExecuteAsync(ir, null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Empty(host.Calls);
    }

    [Fact]
    public void BenchOut_NoOps_Without_Host()
    {
        var g = new ExecutionGlobals { OutputNamespace = "ns", PluginHost = null };
        g.BenchOut("k", "v"); // must not throw
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

    private sealed class RecordingHost : IPluginHost
    {
        public List<(string Plugin, string Method, object?[] Args)> Calls { get; } = [];

        public object? Call(string pluginName, string methodName, params object[] args)
        {
            Calls.Add((pluginName, methodName, args));
            return true;
        }

        public void Notify(string pluginName, string methodName, params object[] args) { }
        public object? CallWithTarget(string pluginName, string methodName, string targetDevice, params object[] args) => null;
        public object? TryGetDevice(string deviceName) => null;
        public bool StartPlugin(string pluginName) => true;
        public bool StopPlugin(string pluginName) => true;
        public bool StopWorkflow(string workflowId) => true;
        public string CreateWorkflow(string name, string source) => string.Empty;
        public bool RunWorkflow(string workflowId) => true;
        public bool InstallPlugin(string kxpPath) => true;
        public string GetPluginInfoByName(string pluginName) => string.Empty;
        public string ListPluginNames() => "[]";
        public string ListWorkflows() => "[]";
    }
}
