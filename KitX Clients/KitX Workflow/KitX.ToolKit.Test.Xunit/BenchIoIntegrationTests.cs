// ─────────────────────────────────────────────────────────────────────────────
// Bench I/O full-chain integration test: trigger params → BenchIn → BenchOut →
// completion-edge packet → downstream BenchIn.
//
// Exercises the real stack with no fakes on the workflow path: KS → IR → .kcs on
// disk → BenchScheduler → BenchWorkflowRunner → WorkflowRunner →
// StructuredRoslynBackend → generated code → ExecutionGlobals.BenchIn/BenchOut →
// bridge host → DataStore. The only stand-in is the DataStoreBridgeHost, which
// mirrors the production PluginHostAdapter's reserved-name interception
// ("KitX.DataStore" → BuiltinDataStorePlugin).
//
// This is the regression anchor for the agreed Bench I/O semantics: the output
// side publishes after the producing statement completes, the input side reads
// only once the upstream run has delivered its packet (scheduler edge + AND-join).
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using KitX.WorkflowV6.Services;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Integration")]
public sealed class BenchIoIntegrationTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _dataStore = new();

    public BenchIoIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kitx-benchio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Trigger_Params_Flow_Through_BenchIn_Out_To_Downstream()
    {
        // A: reads the trigger param, publishes "hello <param>" on its output packet.
        // B: reads A's output key via the completion edge, echoes it into its own
        //    namespace (observable in the DataStore).
        var (registry, lens) = MakeRegistry();
        WriteKcs("a.kcs", lens.Parse(
            "BenchIn(\"userInput\", \"?\") > StringConcat(\"hello \", _) > BenchOut(\"greeting\", _)\n", []));
        WriteKcs("b.kcs", lens.Parse(
            "BenchIn(\"got\", \"none\") > BenchOut(\"echo\", _)\n", []));

        var toolkit = new Toolkit
        {
            Id = "tkbenchio0000000001",
            Meta = new ToolkitMeta { Name = "bench-io" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" },
                new ToolkitWorkflow { Id = "B", Name = "B", File = "b.kcs" },
            ],
            Triggers =
            [
                new Trigger
                {
                    Id = "manual", Type = TriggerType.Manual,
                    Bindings = [new TriggerBinding { Workflow = "A", Params = new() { ["userInput"] = "$payload.value" } }],
                },
                new Trigger
                {
                    Id = "edge", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                    Bindings = [new TriggerBinding { Workflow = "B", Params = new() { ["got"] = "$output.greeting" } }],
                },
            ],
        };

        var host = new DataStoreBridgeHost(new BuiltinDataStorePlugin(_dataStore));
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, host));
        using var scheduler = new BenchScheduler(toolkit, new BenchWorkflowRunner(runner), _dataStore, new ToolkitFileStore(_root));

        var completed = new TaskCompletionSource();
        scheduler.RunCompleted += (_, e) => { if (e.IsSuccess) completed.TrySetResult(); };
        scheduler.StartRun("manual", new { value = "world" });

        var done = await Task.WhenAny(completed.Task, Task.Delay(30_000));
        Assert.True(ReferenceEquals(done, completed.Task), "run did not complete in time");

        var echoKey = _dataStore.Keys().Single(k => k.EndsWith("/wf/B/echo", StringComparison.Ordinal));
        var echoed = _dataStore.Get(echoKey);
        Assert.True(echoed.HasValue, "echo key missing");
        Assert.Equal("hello world", echoed!.Value.GetString());
    }

    private (BuiltinFunctionRegistry Registry, KsTextLens Lens) MakeRegistry()
    {
        var registry = BuiltinFunctionRegistry.Discover(
            typeof(BuiltinFunctionRegistry).Assembly,
            typeof(BuiltinDataStorePlugin).Assembly);
        return (registry, new KsTextLens(registry));
    }

    private void WriteKcs(string fileName, Workflow ir)
    {
        var kcs = new KcsFileFormat
        {
            Id = Path.GetFileNameWithoutExtension(fileName),
            Name = fileName,
            IrData = WorkflowSerializer.Serialize(ir),
            IrVersion = "v6",
        };
        File.WriteAllText(Path.Combine(_root, fileName), JsonSerializer.Serialize(kcs));
    }

    /// <summary>Mirrors the production PluginHostAdapter's reserved-name interception
    /// for "KitX.DataStore"; everything else is a null response (safe default).</summary>
    private sealed class DataStoreBridgeHost : IPluginHost
    {
        private readonly BuiltinDataStorePlugin _plugin;
        public DataStoreBridgeHost(BuiltinDataStorePlugin plugin) => _plugin = plugin;

        public object? Call(string pluginName, string methodName, params object[] args)
            => pluginName == BuiltinDataStorePlugin.PluginName && _plugin.HasMethod(methodName)
                ? _plugin.Invoke(methodName, args)
                : null;

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
