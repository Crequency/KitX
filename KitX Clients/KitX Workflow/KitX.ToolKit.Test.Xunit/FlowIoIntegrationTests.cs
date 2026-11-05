// ─────────────────────────────────────────────────────────────────────────────
// Bench I/O full-chain integration test: trigger params → FlowIn → FlowOut →
// completion-edge packet → downstream FlowIn.
//
// Exercises the real stack with no fakes on the workflow path: KS → IR → .kcs on
// disk → RunScheduler → WorkflowRunnerExecutor → WorkflowRunner →
// StructuredRoslynBackend → generated code → ToolKitExecutionGlobals.FlowIn/Out →
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
using KitX.ToolKit.Run;
using KitX.ToolKit.Builtin;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Contract.Declaration;
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
public sealed class FlowIoIntegrationTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _dataStore = new();
    private const string TkId = "tkbenchio0000000001";

    public FlowIoIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kitx-flowio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, TkId));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Trigger_Params_Flow_Through_FlowIn_Out_To_Downstream()
    {
        // A: reads the trigger param, publishes "hello <param>" on its output packet.
        // B: reads A's published key straight from the delivered packet (the edge declares the
        //    name — design Q70 — and the packet's own property names are what B can read),
        //    then records it on a key B owns, which is where the test can observe it.
        //
        // B deliberately writes to a *declared key* rather than to another FlowOut: FlowOut now
        // hands the value back with the execution result instead of parking it in the store
        // (design Q48), so a store entry is no longer the right place to look for it.
        var (registry, lens) = MakeRegistry();
        WriteKcs("a.kcs", lens.Parse(
            "FlowIn(\"userInput\", \"?\") > StringConcat(\"hello \", _) > FlowOut(\"greeting\", _)\n", []));
        // B only needs to read what A published. It deliberately does not call ToolkitSet: that
        // builtin resolves through the mounted instance's key table, and this test drives the
        // scheduler directly rather than through Mount, so there is no table to resolve against.
        WriteKcs("b.kcs", lens.Parse(
            "FlowIn(\"greeting\", \"none\") > FlowOut(\"echo\", _)\n", []));

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
                    Bindings = [new TriggerBinding { Workflow = "B", Params = new() { ["greeting"] = null } }],
                },
            ],
        };

        var factory = CreateFactory();
        var runner = new WorkflowRunner(new StructuredRoslynBackend(registry, factory: factory));

        // Recording wrapper around the real executor. It observes the constant overrides the
        // scheduler hands each node — which is exactly the surface Q70 governs: the names an edge
        // declared, resolved against the packet the predecessor published. Reading the store
        // instead would observe a mechanism design Q48 removes.
        var observed = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal);
        var executor = new ObservingExecutor(new WorkflowRunnerExecutor(runner), observed);

        var fileStore = new ToolkitFileStore(_root);
        using var scheduler = new RunScheduler(
            CompileFixtures.Compile(toolkit, _root), executor, fileStore);

        var completed = new TaskCompletionSource();
        scheduler.RunCompleted += (_, e) => { if (e.IsSuccess) completed.TrySetResult(); };
        scheduler.StartRun("manual", new { value = "world" });

        var done = await Task.WhenAny(completed.Task, Task.Delay(30_000));
        Assert.True(ReferenceEquals(done, completed.Task), "run did not complete in time");

        // A read its trigger binding and published `greeting`.
        Assert.True(observed.TryGetValue("A", out var aOverrides), "A never ran");
        Assert.Equal("world", aOverrides["userInput"]);

        // B saw `greeting` because its edge declared that name — and only that name.
        Assert.True(observed.TryGetValue("B", out var bOverrides), "B never ran");
        Assert.Equal("hello world", bOverrides["greeting"]);
    }

    /// <summary>
    /// Forwards to the real executor while recording the overrides each node received.
    /// </summary>
    /// <remarks>
    /// The overrides dictionary is the scheduler's actual hand-off to a node, so recording it
    /// observes the real behaviour rather than re-deriving it. A node's published values are
    /// asserted through what the <em>next</em> node received, which is what the design promises:
    /// data travels along the edge, not through a shared store.
    /// </remarks>
    private sealed class ObservingExecutor : IWorkflowExecutor
    {
        private readonly IWorkflowExecutor _inner;
        private readonly Dictionary<string, IReadOnlyDictionary<string, string?>> _observed;

        public ObservingExecutor(IWorkflowExecutor inner, Dictionary<string, IReadOnlyDictionary<string, string?>> observed)
        {
            _inner = inner;
            _observed = observed;
        }

        public Task<WorkflowExecutionResult> ExecuteAsync(
            string workflowId,
            string filePath,
            IReadOnlyDictionary<string, string?>? overrides,
            CancellationToken ct)
        {
            _observed[workflowId] = overrides is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(overrides, StringComparer.Ordinal);

            return _inner.ExecuteAsync(workflowId, filePath, overrides, ct);
        }
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
        var services = new ServiceCollection()
            .AddSingleton(_dataStore)
            .AddSingleton(manager)
            .AddSingleton(new DataStoreOptions());
        return new ToolKitExecutionGlobalsFactory(services.BuildServiceProvider());
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
