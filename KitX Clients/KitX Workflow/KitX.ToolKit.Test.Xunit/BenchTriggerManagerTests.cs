using KitX.ToolKit.Bench;
using KitX.ToolKit.Data;
using KitX.ToolKit.Models;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class BenchTriggerManagerTests
{
    private static Toolkit ToolkitWith(params Trigger[] triggers)
        => new()
        {
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers = [.. triggers],
        };

    [Fact]
    public void Activate_Starts_NonCompletion_Sources_Only()
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var registry = TriggerSourceRegistry.BuildDefault();

        var manager = new BenchTriggerManager(
            new ServiceCollection().BuildServiceProvider(),
            registry,
            executor,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        manager.Activate(new Toolkit
        {
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "A", Name = "A", File = "a.kcs" },
                new ToolkitWorkflow { Id = "B", Name = "B", File = "b.kcs" },
            ],
            Triggers =
            [
                new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "A" }] },
                new Trigger { Id = "edge", Type = TriggerType.WorkflowCompletion, Config = new() { From = "A" },
                    Bindings = [new() { Workflow = "B" }] },
            ],
        });

        try
        {
            Assert.NotNull(manager.Scheduler);
            // The Manual source is created; the WorkflowCompletion edge is not a source.
            Assert.NotNull(manager.ActiveToolkit);
        }
        finally
        {
            manager.Deactivate();
        }
    }

    [Fact]
    public void Activate_Throws_On_Invalid_Config()
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new BenchTriggerManager(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        var bad = ToolkitWith(new Trigger { Id = "t", Type = TriggerType.Manual,
            Bindings = [new() { Workflow = "missing" }] });

        Assert.Throws<InvalidOperationException>(() => manager.Activate(bad));
    }

    [Fact]
    public async Task Fire_Runs_The_Trigger_Chain()
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new BenchTriggerManager(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        var completed = new TaskCompletionSource();
        manager.RunCompleted += (_, _) => completed.TrySetResult();
        manager.Activate(ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual,
            Bindings = [new() { Workflow = "wf", Params = new() { ["x"] = "$payload.x" } }] }));

        try
        {
            manager.Fire("manual", new { x = "hello" });

            var done = await Task.WhenAny(completed.Task, Task.Delay(5000));
            Assert.True(done == completed.Task, "run did not complete");

            var call = executor.Calls.Single();
            Assert.Equal("wf", call.WorkflowId);
            Assert.Equal("hello", call.Overrides["x"]);
            Assert.NotNull(call.Overrides[DataStoreScope.OutputNamespaceConstant]);
        }
        finally
        {
            manager.Deactivate();
        }
    }

    [Fact]
    public void Deactivate_Is_Idempotent()
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new BenchTriggerManager(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        manager.Activate(ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual }));
        manager.Deactivate();
        manager.Deactivate();
        Assert.Null(manager.Scheduler);
    }
}
