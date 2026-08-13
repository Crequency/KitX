using KitX.ToolKit.Bench;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class ToolkitInstanceManagerTests
{
    private static Toolkit ToolkitWith(params Trigger[] triggers)
        => new()
        {
            Id = "tk-demo",
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers = [.. triggers],
        };

    private static (ToolkitInstanceManager Manager, RecordingExecutor Executor) Build(Toolkit toolkit)
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));
        return (manager, executor);
    }

    [Fact]
    public void Mount_Starts_Spawn_Sources_Only()
    {
        var tk = new Toolkit
        {
            Id = "tk-demo",
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
        };
        var (manager, _) = Build(tk);

        manager.Mount(tk);

        try
        {
            Assert.True(manager.IsMounted("tk-demo"));
            Assert.Single(manager.MountedToolkitIds);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public void Mount_Throws_On_Invalid_Config()
    {
        var (manager, _) = Build(ToolkitWith());
        var bad = ToolkitWith(new Trigger { Id = "t", Type = TriggerType.Manual,
            Bindings = [new() { Workflow = "missing" }] });
        Assert.Throws<InvalidOperationException>(() => manager.Mount(bad));
    }

    [Fact]
    public void Mount_Is_Idempotent()
    {
        var (manager, _) = Build(ToolkitWith());
        var tk = ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual });
        manager.Mount(tk);
        manager.Mount(tk);
        try
        {
            Assert.Single(manager.MountedToolkitIds);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Spawn_Runs_Chain_And_Transitions_To_Completed()
    {
        var (manager, executor) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "wf", Params = new() { ["x"] = "$payload.x" } }] }));

        var completed = new TaskCompletionSource();
        manager.BenchEvent += (_, e) =>
        {
            if (e is Contracts.Events.InstanceCompletedEvent)
                completed.TrySetResult();
        };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "wf", Params = new() { ["x"] = "$payload.x" } }] }));

        try
        {
            var initiator = new Initiator("fp-abc", "dev-1");
            var id = manager.Spawn("tk-demo", "manual", new { x = "hello" }, initiator);
            Assert.NotNull(id);

            var done = await Task.WhenAny(completed.Task, Task.Delay(5000));
            Assert.True(done == completed.Task, "instance did not complete");

            var call = executor.Calls.Single();
            Assert.Equal("wf", call.WorkflowId);
            Assert.Equal("hello", call.Overrides["x"]);
            Assert.Equal("fp-abc", call.Overrides[InitiatorConstants.DeviceId]);
            Assert.Equal("dev-1", call.Overrides[InitiatorConstants.DeviceName]);

            var snap = manager.Instances.Single();
            Assert.Equal(InstanceStatus.Completed, snap.Status);
            Assert.Equal("fp-abc", snap.Initiator.DeviceId);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Spawn_Creates_Isolated_Instances()
    {
        var (manager, executor) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        var completed = new TaskCompletionSource();
        int remaining = 2;
        manager.BenchEvent += (_, e) =>
        {
            if (e is Contracts.Events.InstanceCompletedEvent && Interlocked.Decrement(ref remaining) == 0)
                completed.TrySetResult();
        };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        try
        {
            var a = manager.Spawn("tk-demo", "manual", null, new Initiator("fp-a", "dev-a"));
            var b = manager.Spawn("tk-demo", "manual", null, new Initiator("fp-b", "dev-b"));
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotEqual(a, b);

            await Task.WhenAny(completed.Task, Task.Delay(5000));

            var namespaces = executor.Calls
                .Select(c => c.Overrides[DataStoreScope.OutputNamespaceConstant]!)
                .ToList();
            Assert.Equal(2, namespaces.Distinct().Count());
            Assert.Equal(2, manager.Instances.Count);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public void Spawn_Rejects_When_MaxInstances_Exceeded()
    {
        var tk = ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] });
        tk.MaxInstances = 1;

        // Hold the first instance in Running so the cap is still hit on the second spawn.
        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        manager.Mount(tk);

        try
        {
            var first = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(first);
            // Second spawn exceeds the cap of 1 running instance.
            var second = manager.Spawn("tk-demo", "manual");
            Assert.Null(second);
        }
        finally
        {
            hold.TrySetResult();
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public void Spawn_Returns_Null_For_Unknown_Or_Intra_Trigger()
    {
        var tk = new Toolkit
        {
            Id = "tk-demo",
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
        };
        var (manager, _) = Build(tk);

        manager.Mount(tk);

        try
        {
            Assert.Null(manager.Spawn("tk-demo", "nope"));
            Assert.Null(manager.Spawn("tk-demo", "edge")); // WorkflowCompletion is intra
            Assert.Null(manager.Spawn("tk-missing", "manual"));
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public void EndInstance_Removes_And_Unmount_Ends_All()
    {
        var (manager, _) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        try
        {
            var id = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id);
            manager.EndInstance(id!);
            Assert.Empty(manager.Instances);

            var id2 = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id2);
            manager.Unmount("tk-demo");
            Assert.Empty(manager.Instances);
            Assert.False(manager.IsMounted("tk-demo"));
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }
}
