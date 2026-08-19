// ─────────────────────────────────────────────────────────────────────────────
// Bench I/O full-chain integration test: trigger params → BenchIn → BenchOut →
// completion-edge packet → downstream BenchIn.
//
// Exercises the real stack with no fakes on the workflow path: KS → IR → .kcs on
// disk → BenchScheduler → BenchWorkflowRunner → WorkflowRunner →
// StructuredRoslynBackend → generated code → ToolKitExecutionGlobals.BenchIn/Out →
// DataStore. The ToolKit first-class builtins are provided by the
// ToolKitExecutionGlobalsFactory (the factory the host DI registers), so no
// reserved-name plugin bridge is involved.
//
// This is the regression anchor for the agreed Bench I/O semantics: the output
// side publishes after the producing statement completes, the input side reads
// only once the upstream run has delivered its packet (scheduler edge + AND-join).
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Panels;
using KitX.ToolKit.Triggers;
using KitX.WorkflowV6.Backend.RoslynBackend;
using KitX.WorkflowV6.Backend.Runtime;
using KitX.WorkflowV6.Builtin;
using KitX.WorkflowV6.Ir;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using KitX.WorkflowV6.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Integration")]
public sealed class BenchIoIntegrationTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _dataStore = new();
    private const string TkId = "tkbenchio0000000001";

    public BenchIoIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kitx-benchio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, TkId));
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
            Id = TkId,
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

        var factory = CreateFactory();
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));
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

    /// <summary>The ToolKit execution stack for Bench I/O: no panel is touched, so the
    /// manager is only needed to satisfy the factory's constructor — its executor is a no-op.</summary>
    private ToolKitExecutionGlobalsFactory CreateFactory()
    {
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new NoOpExecutor(),
            _dataStore,
            _ => new ToolkitFileStore(_root));
        return new ToolKitExecutionGlobalsFactory(_dataStore, new PanelRuntime(_dataStore, manager), manager);
    }

    private (BuiltinFunctionRegistry Registry, KsTextLens Lens) MakeRegistry()
    {
        var registry = BuiltinFunctionRegistry.Discover(
            typeof(BuiltinFunctionRegistry).Assembly,
            typeof(ToolKitExecutionGlobals).Assembly);
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
        File.WriteAllText(Path.Combine(_root, TkId, fileName), JsonSerializer.Serialize(kcs));
    }

    /// <summary>A no-op executor so the manager never runs a workflow (none is spawned here).</summary>
    private sealed class NoOpExecutor : IWorkflowExecutor
    {
        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId, string filePath,
            IReadOnlyDictionary<string, string?>? overrides, CancellationToken ct)
            => Task.FromResult(new WorkflowExecutionResult(workflowId, true, null, null));
    }
}
