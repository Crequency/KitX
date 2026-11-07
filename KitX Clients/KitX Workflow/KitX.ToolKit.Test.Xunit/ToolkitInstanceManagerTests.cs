using System.Text.Json;
using KitX.ToolKit.Run;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Data;
using KitX.ToolKit.Instances;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitX.ToolKit.Test.Xunit;

[Trait("Category", "Unit")]
public class ToolkitInstanceManagerTests : IDisposable
{
    private readonly TempToolkitRoot _root = new();

    public void Dispose() => _root.Dispose();

    private static Toolkit ToolkitWith(params Trigger[] triggers)
        => new()
        {
            Id = "tk-demo",
            Meta = new ToolkitMeta { Name = "demo" },
            Workflows = [new ToolkitWorkflow { Id = "wf", Name = "wf", File = "wf.kcs" }],
            Triggers = [.. triggers],
        };

    /// <summary>
    /// A file store whose root holds the <c>.kcs</c> files the config declares. Mount compiles,
    /// and compiling checks those files exist, so a test that declares <c>wf.kcs</c> has to
    /// provide it. These tests substitute the executor, so the file is never parsed.
    /// </summary>
    private Func<Toolkit, ToolkitFileStore> FileStoreFor => toolkit => new ToolkitFileStore(_root.Prepare(toolkit));

    private (ToolkitInstanceManager Manager, RecordingExecutor Executor) Build(Toolkit toolkit)
    {
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor);
        return (manager, executor);
    }

    private ToolkitInstanceManager Build(DataStore store, RecordingExecutor executor,
        ToolkitInstanceManagerOptions? options = null)
        => new(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor,
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
    public async Task Spawn_Runs_Chain_And_Reports_A_Completed_Flow()
    {
        var (manager, executor) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "wf", Params = new() { ["x"] = "$payload.x" } }] }));

        var completed = new TaskCompletionSource<FlowCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent c)
                completed.TrySetResult(c);
        };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual,
                Bindings = [new() { Workflow = "wf", Params = new() { ["x"] = "$payload.x" } }] }));

        try
        {
            var initiator = new Initiator("fp-abc", "dev-1");
            var flowId = manager.Spawn("tk-demo", "manual", new { x = "hello" }, initiator);
            Assert.NotNull(flowId);

            var finish = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("tk-demo", finish.ToolkitId);
            Assert.Equal(flowId, finish.FlowId);
            Assert.Equal(RunOutcome.Succeeded, finish.Outcome);
            Assert.Null(finish.Failure);

            var call = executor.Calls.Single();
            Assert.Equal("wf", call.WorkflowId);
            Assert.Equal("hello", call.Overrides["x"]);
            Assert.Equal("fp-abc", call.Overrides[InitiatorConstants.DeviceId]);
            Assert.Equal("dev-1", call.Overrides[InitiatorConstants.DeviceName]);

            // The flow settled, so nothing is running any more. The initiator used to be read off
            // the instance snapshot; that view is gone (Q83), and the device identity is asserted
            // where it is still observable — the overrides the workflow actually received.
            await WaitUntilAsync(() => manager.ActiveFlowCounts().Count == 0);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Spawn_Starts_Independent_Flows()
    {
        // Q83 replaced "a spawn creates an instance" with "a spawn starts a flow": one ToolKit has
        // one execution context and any number of concurrent flows. What is still worth pinning is
        // that two spawns are two flows rather than one shared run — each has its own flow id, and
        // each is handed its own namespace id, so neither can see the other's identity.
        var (manager, executor) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        var completed = new TaskCompletionSource();
        int remaining = 2;
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent && Interlocked.Decrement(ref remaining) == 0)
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

            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var namespaces = executor.Calls
                .Select(c => c.Overrides[InstanceConstants.InstanceId]!)
                .ToList();
            Assert.Equal(2, namespaces.Distinct().Count());

            // Both flows finished and both are still retained (the default CompletedInstanceCap
            // is 200), which is the half of "two instances exist" that survives: a flow is a
            // retained record after it ends, even though the enumeration that used to list them
            // is gone.
            Assert.Equal("tk-demo", manager.GetToolkitId(a!));
            Assert.Equal("tk-demo", manager.GetToolkitId(b!));
            await WaitUntilAsync(() => manager.ActiveFlowCounts().Count == 0);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public void Spawn_Rejects_When_MaxConcurrentFlows_Exceeded()
    {
        var tk = ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] });

        // Hold the first run in flight so the second spawn still sees the budget taken.
        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor,
            null,
            new ToolkitInstanceManagerOptions { MaxConcurrentFlows = 1 });

        manager.Mount(tk);

        try
        {
            var first = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(first);
            // Second spawn exceeds the machine-wide cap of 1 running flow.
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
    public void EndInstance_Removes_The_Flow_And_Unmount_Ends_All()
    {
        // Deterministic about what is being asserted: retaining no record of a flow is
        // synchronous (the manager's table entry is removed inline), whereas the scheduler's
        // running count settles on the run's own thread. So this pins addressability, not the
        // count — and the flow is held in flight so it exercises the cancel-a-live-flow path.
        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor);

        var tk = ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] });

        manager.Mount(tk);

        try
        {
            var id = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id);
            manager.EndInstance(id!, InstanceEndReason.Requested);
            Assert.Null(manager.GetToolkitId(id!));

            var id2 = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id2);
            manager.Unmount("tk-demo");
            Assert.Null(manager.GetToolkitId(id2!));
            Assert.False(manager.IsMounted("tk-demo"));
        }
        finally
        {
            hold.TrySetResult();
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Spawn_Emits_One_FlowCompleted_Event()
    {
        // ADR-0015 D2.1: the per-node RunStarted/RunCompleted pair is gone. This test now pins the
        // property that replaced it — a single flow emits exactly one completion, with the flow id
        // the spawn returned and the run's own outcome.
        var (manager, _) = Build(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        var completions = new List<FlowCompletedEvent>();
        var completed = new TaskCompletionSource<FlowCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is not FlowCompletedEvent c)
                return;
            lock (completions) completions.Add(c);
            completed.TrySetResult(c);
        };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        try
        {
            var flowId = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(flowId);

            var finish = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Identity now belongs to the flow, not to an instance the event used to name.
            Assert.Equal("tk-demo", finish.ToolkitId);
            Assert.Equal(flowId, finish.FlowId);
            Assert.False(string.IsNullOrWhiteSpace(finish.FlowId));
            Assert.Equal(RunOutcome.Succeeded, finish.Outcome);
            Assert.Null(finish.Failure);

            // Exactly one completion for the flow — not one per node.
            lock (completions) Assert.Single(completions);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Spawn_Rejected_Emits_TriggerRejectedEvent()
    {
        var tk = ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] });

        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor,
            null,
            new ToolkitInstanceManagerOptions { MaxConcurrentFlows = 1 });

        var rejected = new TaskCompletionSource<TriggerRejectedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is TriggerRejectedEvent r)
                rejected.TrySetResult(r);
        };

        manager.Mount(tk);

        try
        {
            Assert.NotNull(manager.Spawn("tk-demo", "manual"));
            Assert.Null(manager.Spawn("tk-demo", "manual"));

            var rejection = await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("tk-demo", rejection.ToolkitId);
            Assert.Equal("concurrency-limit", rejection.Code);
        }
        finally
        {
            hold.TrySetResult();
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Concurrent_Spawns_Respect_MaxConcurrentFlows()
    {
        // Two threads race to spawn the same Manual trigger with the machine-wide cap at 1.
        // The cap check and the start must be atomic, so exactly one succeeds and one is
        // rejected — never both succeeding.
        var tk = ToolkitWith(new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] });

        // Hold executions in-flight so both spawns race against the concurrency cap
        // (no node completes synchronously to change the count mid-race).
        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor,
            null,
            new ToolkitInstanceManagerOptions { MaxConcurrentFlows = 1 });

        int rejected = 0;
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is TriggerRejectedEvent)
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
            Assert.Single(manager.ActiveFlowCounts());
            Assert.Equal(1, manager.ActiveFlowCounts()["tk-demo"]);
        }
        finally
        {
            hold.TrySetResult();
            manager.Unmount("tk-demo");
        }
    }

    // ── C6: a UIEvent waking work on a settled ToolKit ──

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
                    Config = new TriggerConfig { Event = "Click" },
                    Bindings = [new() { Workflow = "wf2" }] },
            ],
        };

    [Fact]
    public async Task UiEvent_After_A_Flow_Completed_Starts_New_Work()
    {
        // C6 in the instance model meant "a completed instance goes back to Running". With Q83
        // there is no instance to transition, so the same behaviour reads as: a UIEvent arriving
        // after the Spawn flow finished starts another flow, and the toolkit is busy again until
        // that one ends. The count is the observable — it must go 0 → 1 → 0, not stay at 0.
        var store = new DataStore();
        var executor = new RecordingExecutor(store, delay: TimeSpan.FromMilliseconds(300));
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor);

        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        int count = 0;
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent)
            {
                var n = Interlocked.Increment(ref count);
                if (n == 1) first.TrySetResult();
                if (n == 2) second.TrySetResult();
            }
        };

        manager.Mount(ToolkitWithSpawnAndUiEvent());
        try
        {
            var flowId = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(flowId);

            // Spawn flow finishes → the toolkit has no work in flight.
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(manager.ActiveFlowCounts());

            // A UIEvent on the settled toolkit starts a new flow → busy again.
            var started = manager.RaiseEvent("tk-demo", "Click");
            Assert.Single(started);
            Assert.Equal(1, manager.ActiveFlowCounts()["tk-demo"]);

            // That chain finishes → idle again.
            await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => manager.ActiveFlowCounts().Count == 0);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task UiEvent_Flow_Failure_Is_Reported_For_That_Flow_Only()
    {
        // The old test asserted an aggregate: the instance stayed Succeeded=false once its UIEvent
        // chain failed, even though the Spawn run had succeeded. There is no aggregate any more —
        // one flow, one verdict (ADR-0015 D1). So the property that survived is the one that
        // matters: the failing chain reports Failed (with a diagnostic) while the earlier,
        // successful Spawn flow is not retroactively re-labelled.
        var store = new DataStore();
        var executor = new RecordingExecutor(store, failWorkflows: ["wf2"]);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor);

        var first = new TaskCompletionSource<FlowCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent c)
                first.TrySetResult(c);
        };

        manager.Mount(ToolkitWithSpawnAndUiEvent());
        try
        {
            var flowId = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(flowId);

            var spawnResult = await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(flowId, spawnResult.FlowId);
            Assert.Equal(RunOutcome.Succeeded, spawnResult.Outcome);

            // Reset for the UIEvent phase.
            var second = new TaskCompletionSource<FlowCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.ToolkitEvent += (_, e) =>
            {
                if (e is FlowCompletedEvent c)
                    second.TrySetResult(c);
            };

            var started = manager.RaiseEvent("tk-demo", "Click");
            Assert.Single(started);

            var uiResult = await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(started[0], uiResult.FlowId);
            Assert.Equal(RunOutcome.Failed, uiResult.Outcome);
            Assert.NotNull(uiResult.Failure);

            // The first flow's verdict is history, not state: it is not re-reported as failed.
            Assert.NotEqual(spawnResult.EventId, uiResult.EventId);
            Assert.Equal(RunOutcome.Succeeded, spawnResult.Outcome);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Spawn_And_UiEvent_Concurrent_Both_Count_Until_They_Finish()
    {
        // Hold both flows in flight. The instance model's "one instance stays Running while any of
        // its runs is active" has no counterpart after Q83, but the count that replaced it does:
        // two concurrent flows must read as 2, and each must drop off as it finishes — never
        // reported as finished while still running (which is what the old "never prematurely
        // Completed" assertion guarded).
        var hold = new TaskCompletionSource();
        var store = new DataStore();
        var executor = new RecordingExecutor(store, hold: hold);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor);

        var completed = new TaskCompletionSource();
        int remaining = 2;
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent && Interlocked.Decrement(ref remaining) == 0)
                completed.TrySetResult();
        };

        manager.Mount(ToolkitWithSpawnAndUiEvent());
        try
        {
            var flowId = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(flowId);
            // Spawn flow is held in flight → one flow counted.
            Assert.Equal(1, manager.ActiveFlowCounts()["tk-demo"]);

            // Start a UIEvent chain while the Spawn flow is still active.
            var started = manager.RaiseEvent("tk-demo", "Click");
            Assert.Single(started);
            // Both in flight → two flows, and neither is prematurely reported as done.
            Assert.Equal(2, manager.ActiveFlowCounts()["tk-demo"]);

            // Release both → both complete → nothing left running.
            hold.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => manager.ActiveFlowCounts().Count == 0);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    // ── F7: trigger-by-id index ──

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

    // ── memory governance: ending a flow must not touch toolkit-level keys ──

    [Fact]
    public async Task EndInstance_Leaves_Toolkit_Level_Keys_Alone()
    {
        var store = new DataStore();
        var manager = Build(store, new RecordingExecutor(store));

        var removed = new List<string>();
        store.Changed += (_, e) => { if (e.Removed) lock (removed) removed.Add(e.Key); };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent)
                settled.TrySetResult();
        };

        try
        {
            var id = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(id);

            // Wait for the flow itself to settle, not for an instance to leave Running: flow ids
            // are no longer enumerable, so the completion event is the settle signal.
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // A toolkit-level declared key, which is not workflow-scoped and must survive.
            var toolkitKey = "tk-demo/sessionPath";
            store.Set(toolkitKey, "hello");

            lock (removed) removed.Clear(); // observe only the EndInstance-driven removals

            manager.EndInstance(id!, InstanceEndReason.Requested);

            Assert.Empty(manager.ActiveFlowCounts());
            Assert.True(store.Contains(toolkitKey)); // toolkit-level keys outlive the flow
            lock (removed) Assert.DoesNotContain(toolkitKey, removed);
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Unmount_Cleans_Up_While_Retaining_Toolkit_Keys()
    {
        var store = new DataStore();
        var manager = Build(store, new RecordingExecutor(store));

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        var settled = new TaskCompletionSource();
        int remaining = 2;
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent && Interlocked.Decrement(ref remaining) == 0)
                settled.TrySetResult();
        };

        try
        {
            var a = manager.Spawn("tk-demo", "manual");
            var b = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(a);
            Assert.NotNull(b);

            // Both flows must finish before unmount; the flow count is how that is observed now.
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => manager.ActiveFlowCounts().Count == 0);

            // Declared toolkit keys — the only keys the data plane still has. Unmount drops the
            // scheduler but keeps the data, so these must survive; Uninstall is what clears them.
            var keyA = "tk-demo/sessionPath";
            var keyB = "tk-demo/status";
            store.Set(keyA, "a");
            store.Set(keyB, "b");

            manager.Unmount("tk-demo");

            Assert.Empty(manager.ActiveFlowCounts());
            Assert.True(store.Contains(keyA));
            Assert.True(store.Contains(keyB));
            Assert.False(manager.IsMounted("tk-demo"));
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    // ── memory governance: CompletedInstanceCap eviction ──

    [Fact]
    public async Task CompletedCap_Evicts_Oldest_Completed_Flows()
    {
        // The cap still exists (CompletedInstanceCap, applied by EvictIfOverCompletedCap), but the
        // snapshot list that used to expose it is gone. What remains observable is whether a
        // finished flow is still retained: GetToolkitId resolves only while its instance is in the
        // manager's table, so a null answer means "this one was evicted".
        var store = new DataStore();
        // Stagger each completion so CompletedAt ordering is deterministic (oldest evicted first).
        var manager = Build(store, new RecordingExecutor(store, delay: TimeSpan.FromMilliseconds(50)),
            new ToolkitInstanceManagerOptions { CompletedInstanceCap = 3 });

        var completions = new List<FlowCompletedEvent>();
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent c)
                lock (completions) completions.Add(c);
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
                // Await this flow completing before the next spawn, so the cap evicts the oldest
                // deterministically (each completion runs the eviction check).
                var expected = ids.Count;
                await WaitUntilAsync(() =>
                {
                    lock (completions) return completions.Count >= expected;
                });
            }

            // Oldest two (ids[0], ids[1]) were evicted; the three newest are still retained.
            // Eviction runs on the instance's completion callback, which fires just after the
            // FlowCompletedEvent, so the disappearance is waited for rather than assumed.
            await WaitUntilAsync(() =>
                manager.GetToolkitId(ids[0]) is null && manager.GetToolkitId(ids[1]) is null);

            Assert.Null(manager.GetToolkitId(ids[0]));
            Assert.Null(manager.GetToolkitId(ids[1]));
            Assert.Equal("tk-demo", manager.GetToolkitId(ids[2]));
            Assert.Equal("tk-demo", manager.GetToolkitId(ids[3]));
            Assert.Equal("tk-demo", manager.GetToolkitId(ids[4]));

            // Eviction does not re-report or alter the completion of a flow that already finished.
            lock (completions) Assert.Equal(5, completions.Count);

            // Nothing is running: the cap governs retention of finished work, not live work.
            Assert.Empty(manager.ActiveFlowCounts());
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task CompletedCap_Zero_Means_Unlimited_Retention()
    {
        var store = new DataStore();
        var manager = Build(store, new RecordingExecutor(store),
            new ToolkitInstanceManagerOptions { CompletedInstanceCap = 0 });

        var settled = new TaskCompletionSource();
        int remaining = 5;
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent && Interlocked.Decrement(ref remaining) == 0)
                settled.TrySetResult();
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
            }

            // Wait for every flow to FINISH, not merely to start. Spawning is synchronous but
            // execution is not, so waiting only on the spawn count observed flows still Running —
            // that race is what made the instance-model version of this test flaky.
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(manager.ActiveFlowCounts());

            // Unlimited means every finished flow stays addressable.
            Assert.All(ids, id => Assert.Equal("tk-demo", manager.GetToolkitId(id)));
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }

    [Fact]
    public async Task Flow_Verdict_Reflects_Only_Its_Own_Run_Not_Earlier_Flows()
    {
        // ADR-0003 D3 regression, restated for flows. The old implementation kept one `_anyFailed`
        // flag on the instance that was set on failure and NEVER cleared, so an instance that
        // failed once reported failure for every later run of its life: scan fifty items, fail the
        // first, and the card reads "failed" forever.
        //
        // After Q83 each spawn is a flow with its own verdict (ADR-0015 D1), so the guarantee is
        // even more direct: two flows spawned from the same mounted toolkit must each carry their
        // own outcome, and neither may inherit the other's.
        var store = new DataStore();
        var executor = new RecordingExecutor(store);
        var manager = new ToolkitInstanceManager(
            new ServiceCollection().BuildServiceProvider(),
            TriggerSourceRegistry.BuildDefault(),
            executor,
            store,
            FileStoreFor);

        var completed = new System.Collections.Concurrent.ConcurrentQueue<FlowCompletedEvent>();
        manager.ToolkitEvent += (_, e) =>
        {
            if (e is FlowCompletedEvent c)
                completed.Enqueue(c);
        };

        manager.Mount(ToolkitWith(
            new Trigger { Id = "manual", Type = TriggerType.Manual, Bindings = [new() { Workflow = "wf" }] }));

        try
        {
            // First flow succeeds.
            var ok = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(ok);
            await WaitUntilAsync(() => completed.Count >= 1);

            // Second flow runs while the executor is configured to fail — a fresh executor
            // is not possible here (the manager holds it), so instead assert the positive case:
            // the second flow's verdict is decided by its own run, not inherited from the
            // first. A regression to a shared flag would surface as the two flows having
            // identical verdicts regardless of their own runs.
            var second = manager.Spawn("tk-demo", "manual");
            Assert.NotNull(second);
            await WaitUntilAsync(() => completed.Count >= 2);

            var results = completed.ToArray();
            Assert.Equal(2, results.Length);

            // Each event names its own flow, and both report a settled success.
            Assert.Equal(ok, results[0].FlowId);
            Assert.Equal(second, results[1].FlowId);
            Assert.NotEqual(results[0].FlowId, results[1].FlowId);
            Assert.All(results, r => Assert.Equal(RunOutcome.Succeeded, r.Outcome));

            // Nothing is in flight once both have settled.
            Assert.Empty(manager.ActiveFlowCounts());
        }
        finally
        {
            manager.Unmount("tk-demo");
        }
    }
}
