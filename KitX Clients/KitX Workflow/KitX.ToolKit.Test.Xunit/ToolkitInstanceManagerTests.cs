using System.Text.Json;
using KitX.ToolKit.Bench;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Contracts.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Models;
using KitX.ToolKit.Panels;
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

    private static ToolkitInstanceManager Build(DataStore store, RecordingExecutor executor,
        ToolkitInstanceManagerOptions? options = null)
        => new(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()),
            null,
            options);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("condition was not satisfied within the timeout");
            await Task.Delay(10);
        }
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

    [Fact]
    public async Task Spawn_Emits_RunStarted_And_RunCompleted_Events()
    {
        var (manager, _) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        var started = new TaskCompletionSource<RunStartedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<RunCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.BenchEvent += (_, e) =>
        {
            if (e is RunStartedEvent s)
                started.TrySetResult(s);
            if (e is RunCompletedEvent c)
                completed.TrySetResult(c);
        };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        try
        {
            var instanceId = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(instanceId);

            var start = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(instanceId, start.InstanceId);
            Assert.Equal("wf", start.WorkflowId);
            Assert.False(string.IsNullOrWhiteSpace(start.RunId));

            var finish = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(instanceId, finish.InstanceId);
            Assert.Equal(start.RunId, finish.RunId);
            Assert.True(finish.Succeeded);
            Assert.Null(finish.Error);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public void Spawn_SilentTrigger_ExposesSilentSurfaceOnSnapshot()
    {
        var tk = ToolkitWith(new Trigger
        {
            Id = "manual",
            Type = TriggerType.Manual,
            Config = new TriggerConfig { Surface = InstanceConstants.SurfaceSilent },
            Bindings = [new() { Workflow = "wf" }],
        });
        var (manager, _) = Build(tk);

        manager.Mount(tk);

        try
        {
            var instanceId = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(instanceId);

            var snapshot = Assert.Single(manager.Instances);
            Assert.Equal(InstanceConstants.SurfaceSilent, snapshot.Surface);
            Assert.True(snapshot.IsSilent);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Spawn_Rejected_Emits_InstanceSpawnRejectedEvent()
    {
        var tk = ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] });
        tk.MaxInstances = 1;

        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        var rejected = new TaskCompletionSource<InstanceSpawnRejectedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.BenchEvent += (_, e) =>
        {
            if (e is InstanceSpawnRejectedEvent r)
                rejected.TrySetResult(r);
        };

        manager.Mount(tk);

        try
        {
            Assert.NotNull(manager.Spawn("tk-demo", "manual"));
            Assert.Null(manager.Spawn("tk-demo", "manual"));

            var rejection = await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("tk-demo", rejection.ToolkitId);
            Assert.Contains("MaxInstances", rejection.Reason);
        }
        finally
        {
            hold.TrySetResult();
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Concurrent_Spawns_Respect_MaxInstances()
    {
        // Two threads race to spawn the same Manual trigger with MaxInstances=1. The cap
        // check and registration must be atomic, so exactly one succeeds and one is
        // rejected — never both succeeding.
        var tk = ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] });
        tk.MaxInstances = 1;

        // Hold executions in-flight so both spawns race against the Running-instances cap
        // (no node completes synchronously to change the count mid-race).
        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        int rejected = 0;
        manager.BenchEvent += (_, e) =>
        {
            if (e is InstanceSpawnRejectedEvent)
                Interlocked.Increment(ref rejected);
        };
        manager.Mount(tk);

        try
        {
            var results = await Task.WhenAll(
                Task.Run(() => manager.Spawn("tk-demo", "manual")),
                Task.Run(() => manager.Spawn("tk-demo", "manual")));

            Assert.Equal(1, results.Count(r => r is not null));
            Assert.Equal(1, rejected);
            Assert.Single(manager.Instances);
        }
        finally
        {
            hold.TrySetResult();
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public void PanelDataStoreWrites_Project_UiControlStateChanged()
    {
        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new RecordingExecutor(store),
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        var events = new List<UiControlStateChangedEvent>();
        manager.BenchEvent += (_, e) =>
        {
            if (e is UiControlStateChangedEvent u)
                events.Add(u);
        };

        store.Set(PanelScope.Key("tk-demo", "inst-1", "input", "value"), "hello");
        store.Set(PanelScope.Key("tk-demo", "inst-1", "switch", "enabled"), true);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, u => u.InstanceId == "inst-1" && u.ControlId == "input" && u.Prop == "value"
                                     && u.Value is { ValueKind: JsonValueKind.String } v && v.GetString() == "hello");
        Assert.Contains(events, u => u.ControlId == "switch" && u.Prop == "enabled");
    }

    [Fact]
    public void PanelDialogRequest_Projects_DialogRequestedEvent()
    {
        var store = new DataStore();
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            new RecordingExecutor(store),
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        DialogRequestedEvent? dialog = null;
        manager.BenchEvent += (_, e) =>
        {
            if (e is DialogRequestedEvent d)
                dialog = d;
        };

        store.Set(
            PanelScope.Key("tk-demo", "inst-1", "ask", "request"),
            JsonSerializer.SerializeToElement(new { message = "choose", buttons = new[] { "是", "否" } }));

        Assert.NotNull(dialog);
        Assert.Equal("inst-1", dialog!.InstanceId);
        Assert.Equal("ask", dialog.ControlId);
        Assert.Equal("choose", dialog.Message);
        Assert.Equal(["是", "否"], dialog.Buttons);
    }

    // ── C6: Completed × UIEvent state-machine aggregation ──

    private static Toolkit ToolkitWithSpawnAndUiEvent()
        => new()
        {
            Id = "tk-demo",
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows =
            [
                new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" },
                new ToolkitWorkflow { Id = "wf2", Name = "wf2", File = "wf2.kcs" },
            ],
            Triggers =
            [
                new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
                new Trigger { Id = "ui", Type = TriggerType.UIEvent,
                    Config = new TriggerConfig { Control = "btn", Event = "Click" },
                    Bindings = [new() { Workflow = "wf2" }] },
            ],
        };

    [Fact]
    public async Task UiEvent_On_Completed_Instance_ReTransitions_To_Running()
    {
        // Delay each execution so the UIEvent run stays in flight long enough to observe
        // the instance back in Running before it completes again.
        var store = new DataStore();
        var executor = new RecordingExecutor(store, delay: TimeSpan.FromMilliseconds(300));
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        int count = 0;
        manager.BenchEvent += (_, e) =>
        {
            if (e is InstanceCompletedEvent)
            {
                var n = Interlocked.Increment(ref count);
                if (n == 1) first.TrySetResult();
                if (n == 2) second.TrySetResult();
            }
        };

        manager.Mount(ToolkitWithSpawnAndUiEvent());
        try
        {
            var id = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id);

            // Spawn run finishes → instance Completed.
            await Task.WhenAny(first.Task, Task.Delay(5000));
            Assert.Equal(InstanceStatus.Completed, manager.Instances.Single().Status);

            // UIEvent on the Completed instance → back to Running (C6 re-transition).
            manager.RaiseControlEvent(id!, "btn", "Click", null);
            Assert.Equal(InstanceStatus.Running, manager.Instances.Single().Status);

            // The UIEvent chain finishes → back to Completed.
            await Task.WhenAny(second.Task, Task.Delay(5000));
            Assert.Equal(InstanceStatus.Completed, manager.Instances.Single().Status);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task UiEvent_Chain_Completion_Aggregates_Succeeded()
    {
        // The UIEvent workflow fails, so the aggregate Succeeded must be false even though
        // the Spawn run succeeded.
        var store = new DataStore();
        var executor = new RecordingExecutor(store, failWorkflows: ["wf2"]);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        var first = new TaskCompletionSource<InstanceCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.BenchEvent += (_, e) =>
        {
            if (e is InstanceCompletedEvent c)
                first.TrySetResult(c);
        };

        manager.Mount(ToolkitWithSpawnAndUiEvent());
        try
        {
            var id = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id);

            var spawnResult = await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(spawnResult.Succeeded);

            // Reset for the UIEvent phase.
            var second = new TaskCompletionSource<InstanceCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.BenchEvent += (_, e) =>
            {
                if (e is InstanceCompletedEvent c)
                    second.TrySetResult(c);
            };

            manager.RaiseControlEvent(id!, "btn", "Click", null);
            var uiResult = await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(uiResult.Succeeded); // aggregate: a run failed → Succeeded=false
            Assert.Equal(InstanceStatus.Completed, manager.Instances.Single().Status);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Spawn_And_UiEvent_Concurrent_Not_Prematurely_Completed()
    {
        // Hold both runs in flight: the instance must stay Running while either run is
        // active, and only reach Completed once the last one finishes.
        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            _ => new ToolkitFileStore(Path.GetTempPath()));

        var completed = new TaskCompletionSource();
        manager.BenchEvent += (_, e) =>
        {
            if (e is InstanceCompletedEvent)
                completed.TrySetResult();
        };

        manager.Mount(ToolkitWithSpawnAndUiEvent());
        try
        {
            var id = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id);
            // Spawn run is held in flight → instance Running.
            Assert.Equal(InstanceStatus.Running, manager.Instances.Single().Status);

            // Start a UIEvent chain while the Spawn run is still active.
            manager.RaiseControlEvent(id!, "btn", "Click", null);
            // Both runs in flight → still Running, never prematurely Completed.
            Assert.Equal(InstanceStatus.Running, manager.Instances.Single().Status);

            // Release both → both complete → instance Completed.
            hold.TrySetResult();
            await Task.WhenAny(completed.Task, Task.Delay(5000));
            Assert.Equal(InstanceStatus.Completed, manager.Instances.Single().Status);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    // ── F7: trigger-by-id index (instances snapshot surface resolution) ──

    [Fact]
    public void Instances_Snapshot_Resolves_Surface_Per_Trigger_From_Index()
    {
        // Two triggers with distinct surfaces + one with no Surface (null) — the snapshot
        // must resolve each instance's Surface from its trigger via the id index.
        var tk = new Toolkit
        {
            Id = "tk-demo",
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers =
            [
                new Trigger { Id = "silent", Type = TriggerType.Manual,
                    Config = new TriggerConfig { Surface = InstanceConstants.SurfaceSilent },
                    Bindings = [new() { Workflow = "wf" }] },
                new Trigger { Id = "plain", Type = TriggerType.Manual,
                    Config = new TriggerConfig(), // no Surface → null
                    Bindings = [new() { Workflow = "wf" }] },
            ],
        };
        var (manager, _) = Build(tk);

        manager.Mount(tk);
        try
        {
            Assert.NotNull(manager.Spawn("tk-demo", "silent"));
            Assert.NotNull(manager.Spawn("tk-demo", "plain"));

            var snapshots = manager.Instances.ToDictionary(s => s.TriggerId);
            Assert.Equal(InstanceConstants.SurfaceSilent, snapshots["silent"].Surface);
            Assert.True(snapshots["silent"].IsSilent);
            Assert.Null(snapshots["plain"].Surface);
            Assert.False(snapshots["plain"].IsSilent);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public void Mount_Rejects_Duplicate_Trigger_Ids()
    {
        // Config validation guarantees trigger id uniqueness, so a duplicate-id config is
        // rejected at mount time (before the id index is even built).
        var tk = ToolkitWith(
            new Trigger { Id = "dup", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] },
            new Trigger { Id = "dup", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] });

        var (manager, _) = Build(tk);
        Assert.Throws<InvalidOperationException>(() => manager.Mount(tk));
    }

    // ── memory governance: wf-key cleanup on EndInstance / Unmount ──

    [Fact]
    public async Task EndInstance_Clears_Wf_Keys_But_Retains_Panel_Keys()
    {
        var store = new DataStore();
        var manager = Build(store, new RecordingExecutor(store));

        var removed = new List<string>();
        store.Changed += (_, e) => { if (e.Removed) lock (removed) removed.Add(e.Key); };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));
        try
        {
            var id = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id);

            // Wait for the run to finish (the executor writes a wf key automatically) before
            // asserting the cleanup, so the write cannot race the removal.
            await WaitUntilAsync(() => manager.Instances.Any(s => s.InstanceId == id && s.Status == InstanceStatus.Completed));

            // Extra wf keys + a panel key under the instance's namespace.
            var wf1 = DataStoreScope.WorkflowNamespace("tk-demo", id!, "wf1");
            var wf2 = DataStoreScope.WorkflowNamespace("tk-demo", id!, "wf2");
            var panel = PanelScope.Key("tk-demo", id!, "input", "value");
            store.Set(wf1, new { a = 1 });
            store.Set(wf2, new { b = 2 });
            store.Set(panel, "hello");

            lock (removed) removed.Clear(); // observe only the EndInstance-driven removals

            manager.EndInstance(id!);

            Assert.Empty(manager.Instances);
            Assert.False(store.Contains(wf1));
            Assert.False(store.Contains(wf2));
            Assert.True(store.Contains(panel)); // panel keys are retained by design
            lock (removed)
            {
                Assert.Contains(wf1, removed);
                Assert.Contains(wf2, removed);
                Assert.DoesNotContain(panel, removed);
            }
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Unmount_Cleans_All_Instances_Wf_Keys()
    {
        var store = new DataStore();
        var manager = Build(store, new RecordingExecutor(store));

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));
        try
        {
            var a = manager.Spawn("tk-demo", "manual");
            var b = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(a);
            Assert.NotNull(b);

            await WaitUntilAsync(() => manager.Instances.All(s => s.Status == InstanceStatus.Completed));

            var wfA = DataStoreScope.WorkflowNamespace("tk-demo", a!, "wf");
            var wfB = DataStoreScope.WorkflowNamespace("tk-demo", b!, "wf");
            store.Set(DataStoreScope.ScopedKey(wfA, "extra"), 1);
            store.Set(DataStoreScope.ScopedKey(wfB, "extra"), 2);

            manager.Unmount("tk-demo");

            Assert.Empty(manager.Instances);
            Assert.False(store.Contains(wfA));
            Assert.False(store.Contains(wfB));
            Assert.False(manager.IsMounted("tk-demo"));
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    // ── memory governance: CompletedInstanceCap eviction ──

    [Fact]
    public async Task CompletedCap_Evicts_Oldest_Completed_Instances()
    {
        var store = new DataStore();
        // Stagger each completion so CompletedAt ordering is deterministic (oldest evicted first).
        var manager = Build(store, new RecordingExecutor(store, delay: TimeSpan.FromMilliseconds(50)),
            new ToolkitInstanceManagerOptions { CompletedInstanceCap = 3 });

        var cancelled = new List<string>();
        manager.BenchEvent += (_, e) =>
        {
            if (e is InstanceCancelledEvent c)
                lock (cancelled) cancelled.Add(c.InstanceId);
        };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));
        try
        {
            var ids = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var id = manager.Spawn("tk-demo", "manual");
                Assert.NotNull(id);
                ids.Add(id!);
                // Await this instance completing before the next spawn, so the cap evicts the
                // oldest deterministically (each completion runs the eviction check).
                await WaitUntilAsync(() => manager.Instances.Any(s => s.InstanceId == id && s.Status == InstanceStatus.Completed));
            }

            // Oldest two (ids[0], ids[1]) were evicted; the three newest remain.
            var remaining = manager.Instances.Select(s => s.InstanceId).ToList();
            Assert.Equal(3, remaining.Count);
            Assert.DoesNotContain(ids[0], remaining);
            Assert.DoesNotContain(ids[1], remaining);
            Assert.Contains(ids[2], remaining);
            Assert.Contains(ids[3], remaining);
            Assert.Contains(ids[4], remaining);

            lock (cancelled)
            {
                Assert.Equal(2, cancelled.Count);
                Assert.Contains(ids[0], cancelled);
                Assert.Contains(ids[1], cancelled);
            }
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task CompletedCap_Zero_Means_Unlimited()
    {
        var store = new DataStore();
        var manager = Build(store, new RecordingExecutor(store),
            new ToolkitInstanceManagerOptions { CompletedInstanceCap = 0 });

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));
        try
        {
            for (var i = 0; i < 5; i++)
                Assert.NotNull(manager.Spawn("tk-demo", "manual"));

            await WaitUntilAsync(() => manager.Instances.Count == 5);
            Assert.Equal(5, manager.Instances.Count);
            Assert.All(manager.Instances, s => Assert.Equal(InstanceStatus.Completed, s.Status));
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }
}
